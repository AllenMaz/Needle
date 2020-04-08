using Action.Comm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NeedleConsole
{

    public class TableModel
    {
        /// <summary>
        /// 主键
        /// </summary>
        public TablePrimaryKey PrimaryKey { get; set; }
        /// <summary>
        /// 索引
        /// </summary>
        public List<TalbeIndex> Indexs { get; set; }
        /// <summary>
        /// 外键
        /// </summary>
        public List<TableForeignKey> ForeignKeys { get; set; }

        /// <summary>
        /// 架构名称
        /// </summary>
        public string SchemaName { get; set; }
        public string TableName { get; set; }
        public List<TableColumn> Columns { get; set; }
        public TableModel(string schemaName = "dbo")
        {
            this.PrimaryKey = null;
            this.ForeignKeys = new List<TableForeignKey>();
            this.Indexs = new List<TalbeIndex>();
            this.SchemaName = schemaName;
            this.Columns = new List<TableColumn>();
        }

        public void UpgradeTable(DataAccess da)
        {
            List<string> upgradeSqls = new List<string>();

            var sql = @"";
            //升级表步骤
            //查询表是否存在
            sql = @"select * from sysObjects where Id=OBJECT_ID(N'" + this.TableName + "') and xtype='U'";
            Console.WriteLine(sql);
            var existTable = da.GetOneRow(sql);
            if (existTable != null)
            {
                //升级表
                //查询表的所有列信息
                sql = @"
                select t1.name typename --类型名称
                ,t.name  --列名
                ,t.max_length --列的最大长度(-1 = 列数据类型为varchar （max）、 nvarchar （max）、 varbinary （max） 或xml。)
                ,t.precision --精度
                ,t.scale --小数位数
                ,t.is_nullable -- 1 =列可为空
                ,t.is_rowguidcol --1 = 列为声明的 ROWGUIDCOL。
                ,t.is_identity -- 1 = 列具有标识值
                ,t.is_computed -- 1 = 列为计算列
                ,ISNULL(OBJECT_DEFINITION(t.default_object_id),'') defaultvalue --默认值
                ,ISNULL(t2.name,'') defaultconstraints --默认约束
                from sys.columns t
                join sys.types t1 on t.user_type_id = t1.user_type_id
                LEFT JOIN sys.default_constraints t2 ON t.default_object_id = t2.object_id
                where t.object_id = OBJECT_ID('{0}')
                ";
                sql = string.Format(sql,this.TableName);
                var existTableColums = ObjectConvert.Convert<TableColumnProperty>(da.GetDataTable(sql)).ToList();
                if (existTableColums.Count > 0)
                {
                    var existTableColumnNames = existTableColums.Select(v => new { name = v.name});
                    var modelColumnNames = this.Columns.Select(v=> new { name = v.Name});
                    var needUpgradeColumns = existTableColumnNames.Where(v=>modelColumnNames.Contains(v));
                    var needDeleteColumns = existTableColumnNames.Where(v => !modelColumnNames.Contains(v));
                    var needAddColumns = modelColumnNames.Where(v=> !existTableColumnNames.Contains(v));

                    //升级列
                    foreach (var column in needUpgradeColumns)
                    {
                        sql = "";
                        var compareExistColumn = existTableColums.FirstOrDefault(v => v.name == column.name);
                        var compareModelColumn = this.Columns.FirstOrDefault(v => v.Name == column.name);
                        var needupdate = false;
                        if (compareExistColumn.typename.ToLower() != compareModelColumn.DBType.ToString().ToLower() ||
                            compareExistColumn.is_nullable != compareModelColumn.Nullable)
                            needupdate = true;
                        else
                        {
                            //类型相同时判断长度，精度是否相等
                            switch (compareModelColumn.DBType)
                            {
                                case FieldType.Numeric:
                                case FieldType.Decimal:
                                    if (compareExistColumn.precision != compareModelColumn.Precision || compareExistColumn.scale != compareModelColumn.Scale)
                                        needupdate = true;
                                    break;
                                case FieldType.Float:
                                    if (compareExistColumn.scale != compareModelColumn.Scale)
                                        needupdate = true;
                                    break;
                                case FieldType.Nvarchar:
                                    //nvarchar(n) n为字符长度，实际sys.columns 的max_length 为字节数 1字符长度=2字节
                                    var actModelSize = compareModelColumn.Size == -1 ? -1 : compareModelColumn.Size * 2;
                                    if (compareExistColumn.max_length != actModelSize)
                                        needupdate = true;
                                    break;
                                case FieldType.Char:
                                case FieldType.Varchar:
                                case FieldType.Nchar:
                                case FieldType.Binary:
                                case FieldType.Varbinary:
                                    if (compareExistColumn.max_length != compareModelColumn.Size)
                                        needupdate = true;
                                 
                                    break;
                                default:
                                    break;
                            }
                        }
                        if (needupdate)
                        {
                            //修改列
                            sql += "alter table [" + this.SchemaName + "].[" + this.TableName + "] alter column "+this.GenerateUpdateColumnSql(compareModelColumn);
                        }
                        //修改默认值
                        if (!compareModelColumn.Identity)
                        {
                            var modelDefaultValue = FormatTableColumnDefaultValue(compareModelColumn);
                            if (compareExistColumn.defaultvalue != "(" + modelDefaultValue + ")")
                            {
                                //先删除约束
                                if (!string.IsNullOrEmpty(compareExistColumn.defaultconstraints))
                                    sql += "alter table [" + this.SchemaName + "].[" + this.TableName + "] drop constraint " + compareExistColumn.defaultconstraints + "\r\n";
                                sql += "alter table [" + this.SchemaName + "].[" + this.TableName + "] add default (" + modelDefaultValue + ") for " + compareModelColumn.Name;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(sql))
                            upgradeSqls.Add(sql);

                    }
                    //删除列
                    foreach (var column in needDeleteColumns)
                    {
                        sql = "";
                        var compareExistColumn = existTableColums.FirstOrDefault(v => v.name == column.name);
                        if (!string.IsNullOrEmpty(compareExistColumn.defaultconstraints))
                            sql += "alter table [" + this.SchemaName + "].[" + this.TableName + "] drop constraint "+compareExistColumn.defaultconstraints+"\r\n";
                        sql += "alter table [" + this.SchemaName + "].[" + this.TableName + "] drop column "+compareExistColumn.name;
                        upgradeSqls.Add(sql);

                    }
                    //添加列
                    foreach (var column in needAddColumns)
                    {
                        sql = "";
                        var compareModelColumn = this.Columns.FirstOrDefault(v=>v.Name == column.name);
                        sql = "alter table ["+this.SchemaName+"].["+this.TableName+"] add " + this.GenerateCreateColumnSql(compareModelColumn);
                        if (!string.IsNullOrEmpty(compareModelColumn.Desc))
                            sql += "\r\nEXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'" + compareModelColumn.Desc +
                                "' , @level0type=N'SCHEMA',@level0name=N'" + this.SchemaName +
                                "', @level1type=N'TABLE',@level1name=N'" + this.TableName +
                                "', @level2type=N'COLUMN',@level2name=N'" + compareModelColumn.Name + "'";

                        upgradeSqls.Add(sql);
                    }
                    foreach (var usql in upgradeSqls)
                    {
                        Console.WriteLine(usql);
                    }
                }
                else
                    throw new Exception("查询表"+this.TableName+"列信息失败");
                
                
            }
            else
            {
                //创建表
                sql = this.GenerateCreateSql();
                Console.WriteLine(sql);
                da.ExecuteNonQuery(sql);

            }
        }

        /// <summary>
        /// 生成创建语法
        /// </summary>
        /// <returns></returns>
        public string GenerateCreateSql()
        {
            var sql = string.Empty;
            //新建表
            sql = "CREATE TABLE [" + this.SchemaName + "].[" + this.TableName + "]";
            sql += "\r\n(";
            for (var i = 0; i < this.Columns.Count; i++)
            {
                sql += "\r\n\t" + this.GenerateCreateColumnSql(this.Columns[i]);
                if (i != this.Columns.Count - 1)
                    sql += ",";

            }
            sql += "\r\n)";

            //创建主键
            if (this.PrimaryKey != null && this.PrimaryKey.Columns.Count > 0)
            {
                sql += "\r\nALTER TABLE [" + this.SchemaName + "].[" + this.TableName + "] ADD CONSTRAINT [" + this.PrimaryKey.IndexName + "] PRIMARY KEY " +
                    this.PrimaryKey.IndexType.ToString().ToUpper();
                sql += " (";
                for (var i = 0; i < this.PrimaryKey.Columns.Count; i++)
                {
                    if (i > 0)
                        sql += ",";
                    sql += "[" + this.PrimaryKey.Columns[i].Name + "]";
                    if (this.PrimaryKey.Columns[i].Asc)
                        sql += " ASC";
                    else
                        sql += " DESC";
                }
                sql += ")";

            }
            //创建索引
            if (this.Indexs.Count > 0)
            {
                foreach (var index in this.Indexs)
                {
                    sql += "\r\nCREATE " + index.IndexType.ToString().ToUpper() + " INDEX " + index.IndexName + " ON [" + this.SchemaName + "].[" + this.TableName + "]";
                    sql += " (";
                    for (var i = 0; i < index.Columns.Count; i++)
                    {
                        if (i > 0)
                            sql += ",";
                        sql += "[" + index.Columns[i].Name + "]";
                        if (index.Columns[i].Asc)
                            sql += " ASC";
                        else
                            sql += " DESC";
                    }
                    sql += ")";
                }
            }
            //创建外键
            if (this.ForeignKeys.Count > 0)
            {
                foreach (var fk in this.ForeignKeys)
                {
                    sql += "\r\nALTER TABLE [" + this.SchemaName + "].[" + this.TableName + "] ADD CONSTRAINT " + fk.IndexName + " FOREIGN KEY";
                    sql += " (";
                    for (var i = 0; i < fk.Columns.Count; i++)
                    {
                        if (i > 0)
                            sql += ",";
                        sql += "[" + fk.Columns[i].Name + "]";
                    }
                    sql += ")";
                    sql += "\r\nREFERENCES [" + this.SchemaName + "].[" + fk.ReferenceTable + "]";
                    sql += " (";
                    for (var i = 0; i < fk.ReferenceColumns.Count; i++)
                    {
                        if (i > 0)
                            sql += ",";
                        sql += "[" + fk.ReferenceColumns[i].Name + "]";
                    }
                    sql += ")";
                }
            }
            //添加列属性
            foreach (var column in this.Columns)
            {
                if (!string.IsNullOrEmpty(column.Desc))
                    sql += "\r\nEXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'" + column.Desc +
                        "' , @level0type=N'SCHEMA',@level0name=N'" + this.SchemaName +
                        "', @level1type=N'TABLE',@level1name=N'" + this.TableName +
                        "', @level2type=N'COLUMN',@level2name=N'" + column.Name + "'";
            }
            return sql;   
        }

        /// <summary>
        /// 生成更新语法
        /// </summary>
        /// <returns></returns>
        public string GenerateUpdateSql()
        {
            var sql = string.Empty;

            return sql;
        }

        private string GenerateCreateColumnSql(TableColumn field)
        {
            var sql = string.Empty;
            sql = "[" + field.Name + "] [" + field.DBType.ToString().ToUpper() + "]";
            switch (field.DBType)
            {
                case FieldType.Numeric:
                case FieldType.Decimal:
                    sql += "(" + field.Precision + "," + field.Scale + ")";
                    break;
                case FieldType.Float:
                    sql += "(" + field.Scale + ")";
                    break;
                case FieldType.Char:
                case FieldType.Varchar:
                case FieldType.Nchar:
                case FieldType.Nvarchar:
                case FieldType.Binary:
                case FieldType.Varbinary:
                    var size = field.Size.ToString();
                    if (field.Size == -1)
                        size = "max";
                    sql += "(" + size + ")";
                    break;
                default:
                    break;
            }
            //自增
            if (field.Identity)
                sql += " identity(" + field.IdentitySeed + "," + field.IdentityIncrement + ")";
            else
            {
                #region 默认值
                var defaultValue = FormatTableColumnDefaultValue(field);
                sql += " default(" + defaultValue + ")";
                #endregion
            }
            //是否可为空
            if (field.Nullable)
                sql += " null";
            else
                sql += " not null";

            return sql;

        }

        private string GenerateUpdateColumnSql(TableColumn field)
        {
            var sql = string.Empty;
            sql = "[" + field.Name + "] [" + field.DBType.ToString().ToUpper() + "]";
            switch (field.DBType)
            {
                case FieldType.Numeric:
                case FieldType.Decimal:
                    sql += "(" + field.Precision + "," + field.Scale + ")";
                    break;
                case FieldType.Float:
                    sql += "(" + field.Scale + ")";
                    break;
                case FieldType.Char:
                case FieldType.Varchar:
                case FieldType.Nchar:
                case FieldType.Nvarchar:
                case FieldType.Binary:
                case FieldType.Varbinary:
                    var size = field.Size.ToString();
                    if (field.Size == -1)
                        size = "max";
                    sql += "(" + size + ")";
                    break;
                default:
                    break;
            }

            //是否可为空
            if (field.Nullable)
                sql += " null";
            else
                sql += " not null";

            return sql;

        }

        private string FormatTableColumnDefaultValue(TableColumn field)
        {
            var defaultValue = "''";
            switch (field.DBType)
            {
                case FieldType.Numeric:
                case FieldType.Decimal:
                case FieldType.BigInt:
                case FieldType.Float:
                case FieldType.Int:
                case FieldType.Money:
                case FieldType.Real:
                case FieldType.SmallInt:
                case FieldType.SmallMoney:
                case FieldType.TinyInt:
                    defaultValue = "(" + Helper.ToInt32(field.DefaulValue) + ")";
                    break;
            }
            return defaultValue;
        }


    }

    /// <summary>
    /// 表索引
    /// </summary>
    public class TalbeIndex
    {
        /// <summary>
        /// 索引名称
        /// </summary>
        public string IndexName { get; set; }
        /// <summary>
        /// 索引列
        /// </summary>
        public List<GroupColum> Columns { get; set; }
        /// <summary>
        /// 索引类型
        /// </summary>
        public IndexType IndexType { get; set; }
    }
    /// <summary>
    /// 表主键
    /// </summary>
    public class TablePrimaryKey
    {
        /// <summary>
        /// 索引名称
        /// </summary>
        public string IndexName { get; set; }
        /// <summary>
        /// 主键列
        /// </summary>
        public List<GroupColum> Columns { get; set; }
        /// <summary>
        /// 索引类型
        /// </summary>
        public IndexType IndexType { get; set; }

    }
    /// <summary>
    /// 表外键
    /// </summary>
    public class TableForeignKey
    {
        public string IndexName { get; set; }
        /// <summary>
        /// 外键列 
        /// </summary>
        public List<GroupColum> Columns { get; set; }
        /// <summary>
        /// 外键表
        /// </summary>
        public string ReferenceTable { get; set; }
        /// <summary>
        /// 外键关联列
        /// </summary>
        public List<GroupColum> ReferenceColumns { get; set; }

        /// <summary>
        /// 删除外键动作
        /// </summary>
        public ForeignKeyDelOrUpdateOperateType DeleteAction { get; set; }
        /// <summary>
        /// 更新外键动作
        /// </summary>
        public ForeignKeyDelOrUpdateOperateType UpdateAction { get; set; }
    }


    public class GroupColum
    {
        /// <summary>
        /// 字段名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 升序
        /// </summary>
        public bool Asc { get; set; }

        public GroupColum()
        {
            this.Asc = true;
        }
    }
    /// <summary>
    /// 表列
    /// </summary>
    public class TableColumn
    {
        /// <summary>
        /// 列名
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 列描述
        /// </summary>
        public string Desc { get; set; }
        /// <summary>
        /// 数据库类型
        /// </summary>
        public FieldType DBType { get; set; }
        /// <summary>
        /// 可为空
        /// </summary>
        public bool Nullable { get; set; }
        /// <summary>
        /// 精度
        /// </summary>
        public int Precision { get; set; }
        /// <summary>
        /// 小数位数
        /// </summary>
        public int Scale { get; set; }
        /// <summary>
        /// 字段大小
        /// 列的最大长度(-1 = 列数据类型为varchar （max）、 nvarchar （max）、 varbinary （max） 或xml。)
        /// </summary>
        public int Size { get; set; }
        /// <summary>
        /// Identyty(seed,increment)
        /// </summary>
        public bool Identity { get; set; }
        /// <summary>
        /// 自增起始数
        /// </summary>
        public int IdentitySeed { get; set; }
        /// <summary>
        /// 每次自增数
        /// </summary>
        public int IdentityIncrement { get; set; }
        //默认值
        public string DefaulValue { get; set; }


        public TableColumn()
        {
            this.Nullable = false;
            this.Precision = 18;
            this.Scale = 0;
            this.Size = 20;
            this.Identity = false;
            this.IdentitySeed = 1;
            this.IdentityIncrement = 1;
            this.DefaulValue = "";
        }
    }

    /// <summary>
    /// 外键删除或更新操作类型
    /// </summary>
    public enum ForeignKeyDelOrUpdateOperateType
    {
        /// <summary>
        /// 表示 不做任何操作
        /// </summary>
        NoAction,
        /// <summary>
        /// 表示在外键表中将相应字段设置为null
        /// </summary>
        SetNull,
        /// <summary>
        /// 表示设置为默认值
        /// </summary>
        SetDefault,
        /// <summary>
        /// 表示级联操作，就是说，如果主键表中被参考字段更新，外键表中也更新，主键表中的记录被删除，外键表中改行也相应删除
        /// </summary>
        Cascade
    }
    public enum IndexType
    {
        /// <summary>
        /// 唯一索引
        /// </summary>
        UNIQUE,
        /// <summary>
        /// 聚集索引
        /// </summary>
        CLUSTERED,
        /// <summary>
        /// 非聚集索引
        /// </summary>
        NONCLUSTERED
    }
    public enum FieldType
    {
        #region 精确数字
        /// <summary>
        /// -2^63 (-9,223,372,036,854,775,808) 到 2^63-1 (9,223,372,036,854,775,807)
        /// </summary>
        BigInt,
        Numeric,
        Bit,
        /// <summary>
        /// -2^15 (-32,768) 到 2^15-1 (32,767)
        /// </summary>
        SmallInt,
        /// <summary>
        /// decimal[ (p[ ,s] )]  p（精度）1-28 默认18  s（小数位数），0 <= s <= p
        /// </summary>
        Decimal,
        SmallMoney,
        /// <summary>
        /// -2^31 (-2,147,483,648) 到 2^31-1 (2,147,483,647)
        /// </summary>
        Int,
        /// <summary>
        /// 0 到 255
        /// </summary>
        TinyInt,
        Money,
        #endregion
        #region 近似数字
        /// <summary>
        /// float [ (n) ]  范围 1-53  n 的默认值为 53 。
        /// </summary>
        Float,
        Real,
        #endregion
        #region 日期和时间
        Date,
        DateTimeOffset,
        DateTime2,
        SmallDateTime,
        DateTime,
        Time,
        #endregion
        #region 字符串
        Char,
        Varchar,
        Text,
        #endregion
        #region
        Nchar,
        /// <summary>
        /// nvarchar [ ( n | max ) ] 可变大小字符串数据。 n 用于定义字符串大小（以双字节为单位），并且它可能为 1 到 4,000 之间的值 。
        /// max 指示最大存储大小是 2^30-1 个字符 (2 GB) 
        /// </summary>
        Nvarchar,
        Ntext,
        #endregion
        #region 二进制字符串
        Binary,
        Varbinary,
        Image,
        #endregion
        #region 其他数据类型
        Xml
        #endregion
    }

    public class TableColumnProperty
    {
        /// <summary>
        /// 类型名称
        /// </summary>
        public string typename { get; set; }
        /// <summary>
        /// 列名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 列的最大长度(-1 = 列数据类型为varchar （max）、 nvarchar （max）、 varbinary （max） 或xml。)
        /// </summary>
        public int max_length { get; set; }
        /// <summary>
        /// 精度
        /// </summary>
        public int precision { get; set; }
        /// <summary>
        /// 小数位数
        /// </summary>
        public int scale { get; set; }
        /// <summary>
        /// 1 =列可为空
        /// </summary>
        public bool is_nullable { get; set; }
        /// <summary>
        /// 1 = 列为声明的 ROWGUIDCOL
        /// </summary>
        public bool is_rowguidcol { get; set; }
        /// <summary>
        /// 1 = 列具有标识值
        /// </summary>
        public bool is_identity { get; set; }
        /// <summary>
        /// 1 = 列为计算列
        /// </summary>
        public bool is_computed { get; set; }
        /// <summary>
        /// 默认值
        /// </summary>
        public string defaultvalue { get; set; }
        /// <summary>
        /// 默认约束
        /// </summary>
        public string defaultconstraints { get; set; }
    }
}
