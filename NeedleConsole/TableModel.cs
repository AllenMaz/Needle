using Action.Comm;
using System;
using System.Collections.Generic;
using System.Data;
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
        public List<TableIndex> Indexs { get; set; }
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
            this.Indexs = new List<TableIndex>();
            this.SchemaName = schemaName;
            this.Columns = new List<TableColumn>();
        }

        public void UpgradeTable(DataAccess da)
        {
            List<string> upgradeSqls = new List<string>();

            var sql = @"";
            //升级表步骤
            //查询表是否存在
            sql = @"SELECT * FROM sys.objects t WHERE t.type ='u' AND t.object_id = OBJECT_ID("+Parser.QuoteSqlStr("["+this.SchemaName+"].["+this.TableName+"]")+")";
            Console.WriteLine(sql);
            var existTable = da.GetOneRow(sql);
            if (existTable != null)
            {
                //升级表
                var existTableForeignKeys = this.GetTableForeignKeys(da);
                var existTableIndexs = this.GetTableIndexs(da);
                var existTableColums = this.GetTableColumns(da);

                #region 升级列
                if (existTableColums.Count > 0)
                {
                    var existTableColumnNames = existTableColums.Select(v => new { name = v.name });
                    var modelColumnNames = this.Columns.Select(v => new { name = v.Name });
                    var needUpgradeColumns = existTableColumnNames.Where(v => modelColumnNames.Contains(v));
                    var needDeleteColumns = existTableColumnNames.Where(v => !modelColumnNames.Contains(v));
                    var needAddColumns = modelColumnNames.Where(v => !existTableColumnNames.Contains(v));

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
                            sql += "alter table [" + this.SchemaName + "].[" + this.TableName + "] alter column " + this.GenerateUpdateColumnSql(compareModelColumn);
                        }
                        //修改默认值
                        if (!compareModelColumn.Identity)
                        {
                            var modelDefaultValue = FormatTableColumnDefaultValue(compareModelColumn);
                            if (compareExistColumn.defaultvalue != "(" + modelDefaultValue + ")")
                            {
                                //先删除约束
                                if (!string.IsNullOrEmpty(compareExistColumn.defaultconstraints))
                                    sql += this.GenerateDeleteConstraintSql(compareExistColumn.defaultconstraints);
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
                        //删除列相关的约束
                        if (!string.IsNullOrEmpty(compareExistColumn.defaultconstraints))
                        {
                            sql += this.GenerateDeleteConstraintSql(compareExistColumn.defaultconstraints);
                        }
                        //删除相关外键
                        var refForeignKeys = existTableForeignKeys.Where(v => v.Columns.Select(a => a.Name).Contains(compareExistColumn.name))
                            .Select(v => v.IndexName).Distinct();
                        foreach (var fk in refForeignKeys)
                        {
                            sql += this.GenerateDeleteForeignKeySql(fk);

                        }

                        //删除列相关的索引
                        var indexs = existTableIndexs.Where(v => v.Columns.Select(a => a.Name).Contains(compareExistColumn.name))
                            .Select(v => v.IndexName).Distinct();
                        foreach (var idx in indexs)
                        {
                            sql += this.GenerateDeleteIndexSql(idx);

                        }

                        //删除列
                        sql += "alter table [" + this.SchemaName + "].[" + this.TableName + "] drop column " + compareExistColumn.name;
                        upgradeSqls.Add(sql);

                    }
                    //添加列
                    foreach (var column in needAddColumns)
                    {
                        sql = "";
                        var compareModelColumn = this.Columns.FirstOrDefault(v => v.Name == column.name);
                        sql = "alter table [" + this.SchemaName + "].[" + this.TableName + "] add " + this.GenerateCreateColumnSql(compareModelColumn);
                        if (!string.IsNullOrEmpty(compareModelColumn.Desc))
                            sql += "\r\nEXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'" + compareModelColumn.Desc +
                                "' , @level0type=N'SCHEMA',@level0name=N'" + this.SchemaName +
                                "', @level1type=N'TABLE',@level1name=N'" + this.TableName +
                                "', @level2type=N'COLUMN',@level2name=N'" + compareModelColumn.Name + "'";

                        upgradeSqls.Add(sql);
                    }

                }
                else
                    throw new Exception("查询表" + this.TableName + "列信息失败");
                #endregion
                #region 升级表索引
                var existTableIndexNames = existTableIndexs.Select(v => new { name = v.IndexName });
                var modelIndexNames = this.Indexs.Select(v => new { name = v.IndexName });

                var needUpgradeIndexes = existTableIndexNames.Where(v => modelIndexNames.Contains(v));
                var needDeleteIndexes = existTableIndexNames.Where(v => !modelIndexNames.Contains(v));
                var needAddIndexes = modelIndexNames.Where(v => !existTableIndexNames.Contains(v));
                //待升级索引
                foreach (var index in needUpgradeIndexes)
                {
                    sql = "";
                    var compareExistIndex = existTableIndexs.FirstOrDefault(v => v.IndexName == index.name);
                    var compareModelIndex = this.Indexs.FirstOrDefault(v => v.IndexName == index.name);
                    var needupdate = false;
                    if (compareExistIndex.IndexType != compareModelIndex.IndexType ||
                        compareExistIndex.Columns.Count != compareModelIndex.Columns.Count)
                        needupdate = true;
                    else
                    {
                        //判断新旧索引列是否一致
                        for (int i = 0; i < compareExistIndex.Columns.Count(); i++)
                        {
                            if (compareExistIndex.Columns[i].Name != compareModelIndex.Columns[i].Name
                                || compareExistIndex.Columns[i].Asc != compareModelIndex.Columns[i].Asc)
                            {
                                needupdate = true;
                                break;
                            }
                        }
                    }
                    if (needupdate)
                    {
                        //删除旧索引，
                        sql = this.GenerateDeleteIndexSql(index.name);
                        upgradeSqls.Add(sql);
                        //添加新索引
                        sql = this.GenerateCreateIndexSql(compareModelIndex);
                        upgradeSqls.Add(sql);

                    }

                }
                //待删除索引
                foreach (var index in needDeleteIndexes)
                {
                    //删除旧索引，
                    sql = this.GenerateDeleteIndexSql(index.name);
                    upgradeSqls.Add(sql);

                }
                //待新建索引
                foreach (var index in needAddIndexes)
                {
                    var compareModelIndex = this.Indexs.FirstOrDefault(v => v.IndexName == index.name);
                    sql = this.GenerateCreateIndexSql(compareModelIndex);
                    upgradeSqls.Add(sql);
                }
                #endregion

                #region 升级表外键
                var existTableForeignKeyNames = existTableForeignKeys.Select(v => new { name = v.IndexName });
                var modelForeignKeyNames = this.ForeignKeys.Select(v => new { name = v.IndexName });

                var needUpgradeForeignKeyes = existTableForeignKeyNames.Where(v => modelForeignKeyNames.Contains(v));
                var needDeleteForeignKeyes = existTableForeignKeyNames.Where(v => !modelForeignKeyNames.Contains(v));
                var needAddForeignKeyes = modelForeignKeyNames.Where(v => !existTableForeignKeyNames.Contains(v));
                //待升级外键
                foreach (var index in needUpgradeForeignKeyes)
                {
                    sql = "";
                    var compareExistForeignKey = existTableForeignKeys.FirstOrDefault(v => v.IndexName == index.name);
                    var compareModelForeignKey = this.ForeignKeys.FirstOrDefault(v => v.IndexName == index.name);
                    var needupdate = false;
                    if (compareExistForeignKey.ReferenceTable != compareModelForeignKey.ReferenceTable ||
                        compareExistForeignKey.DeleteAction != compareModelForeignKey.DeleteAction ||
                        compareExistForeignKey.UpdateAction != compareModelForeignKey.UpdateAction ||
                        compareExistForeignKey.Columns.Count != compareModelForeignKey.Columns.Count)
                        needupdate = true;
                    else
                    {
                        //判断新旧外键列是否一致
                        for (int i = 0; i < compareExistForeignKey.Columns.Count(); i++)
                        {
                            if (compareExistForeignKey.Columns[i].Name != compareModelForeignKey.Columns[i].Name)
                            {
                                needupdate = true;
                                break;
                            }
                            if (compareExistForeignKey.ReferenceColumns[i].Name != compareModelForeignKey.ReferenceColumns[i].Name)
                            {
                                needupdate = true;
                                break;
                            }
                        }
                    }
                    if (needupdate)
                    {
                        //删除旧的外键
                        sql = this.GenerateDeleteForeignKeySql(index.name);
                        //新增外键
                        sql += this.GenerateCreateForeignKeySql(compareModelForeignKey);
                        upgradeSqls.Add(sql);
                    }
                }
                //待删除外键
                foreach (var index in needDeleteForeignKeyes)
                {
                    sql = this.GenerateDeleteForeignKeySql(index.name);
                    upgradeSqls.Add(sql);
                }
                //待新增外键
                foreach (var index in needAddForeignKeyes)
                {
                    var compareModelForeignKey = this.ForeignKeys.FirstOrDefault(v => v.IndexName == index.name);
                    sql += this.GenerateCreateForeignKeySql(compareModelForeignKey);
                    upgradeSqls.Add(sql);
                }
                #endregion
                //升级表属性

                foreach (var usql in upgradeSqls)
                {
                    Console.WriteLine(usql);
                    da.ExecuteNonQuery(usql);
                }
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
                sql += "\r\n";
                sql += this.GenerateCreateFrimaryKeySql(this.PrimaryKey);

            }
            //创建索引
            if (this.Indexs.Count > 0)
            {
                foreach (var index in this.Indexs)
                {
                    sql += "\r\n";
                    sql += this.GenerateCreateIndexSql(index);
                }
            }
            //创建外键
            if (this.ForeignKeys.Count > 0)
            {
                foreach (var fk in this.ForeignKeys)
                {
                    sql += "\r\n";
                    sql += this.GenerateCreateForeignKeySql(fk) ;
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

        private string GenerateCreateForeignKeySql(TableForeignKey fk)
        {
            var sql = "ALTER TABLE [" + this.SchemaName + "].[" + this.TableName + "] ADD CONSTRAINT " + fk.IndexName + " FOREIGN KEY";
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
            sql += "\r\nON DELETE " + fk.DeleteAction.ToString();
            sql += "\r\nON UPDATE " + fk.DeleteAction.ToString();

            return sql;
        }

        private string GenerateDeleteForeignKeySql(string name)
        {
            //删外键
            var sql = "IF EXISTS(SELECT * from sys.objects t WHERE t.type = 'F' AND t.object_id = OBJECT_ID('" + name + "'))";
            sql += "\r\n alter table [" + this.SchemaName + "].[" + this.TableName + "] drop constraint " + name+"\r\n";
            return sql;
        }

        private string GenerateCreateIndexSql(TableIndex index)
        {
            var sql = "CREATE " + index.IndexType.ToString().ToUpper() + " INDEX " + index.IndexName + " ON [" + this.SchemaName + "].[" + this.TableName + "]";
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
            return sql;
        }

        private string GenerateDeleteIndexSql(string name)
        {
            var sql = "IF EXISTS(SELECT * from sys.indexes t WHERE t.object_id = OBJECT_ID(" +
                Parser.QuoteSqlStr("[" + this.SchemaName + "].[" + this.TableName + "]") + ") and t.name = " + Parser.QuoteSqlStr(name) + ")";
            sql += "\r\n drop index " + name + " on [" + this.SchemaName + "].[" + this.TableName + "]\r\n";
            return sql;
        }

        private string GenerateCreateFrimaryKeySql(TablePrimaryKey key)
        {
            var sql = "ALTER TABLE [" + this.SchemaName + "].[" + this.TableName + "] ADD CONSTRAINT [" + key.IndexName + "] PRIMARY KEY " +
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
            return sql;
        }

        private string GenerateDeleteConstraintSql(string name)
        {
            var sql = "IF EXISTS(SELECT * from sys.objects t WHERE t.type = 'D' AND t.object_id = OBJECT_ID('" + name + "'))";
            sql += "\r\n alter table [" + this.SchemaName + "].[" + this.TableName + "] drop constraint " + name +"\r\n";
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

        #region 查询表信息方法
        /// <summary>
        /// 查询表外键信息
        /// </summary>
        /// <param name="da"></param>
        /// <returns></returns>
        private List<TableForeignKey> GetTableForeignKeys(DataAccess da)
        {
            List<TableForeignKey> tableForeignKeys = new List<TableForeignKey>();
           //查询表的所有外键信息
           var sql = @"
                SELECT 
                PT.name parent_table_name --引用外键表名
                ,PC.name parent_column_name --引用外键列名
                ,RT.name referenced_table_name --被引用外键表名
                ,RC.name referenced_column_name --被引用外键列名
                ,FK.name foreignkeyname --外键名 
                ,FK.delete_referential_action
				,FK.delete_referential_action_desc
				,FK.update_referential_action
				,FK.update_referential_action_desc
                FROM sys.foreign_key_columns t
                JOIN sys.objects PT ON t.parent_object_id=PT.object_id
                JOIN sys.objects RT ON t.referenced_object_id=RT.object_id
                JOIN sys.columns PC ON t.parent_object_id=PC.object_id AND t.parent_column_id=PC.column_id
                JOIN sys.columns RC ON t.referenced_object_id=RC.object_id AND t.referenced_column_id=RC.column_id
                JOIN sys.foreign_keys FK ON t.parent_object_id = fk.parent_object_id AND t.constraint_object_id = fk.object_id
                WHERE t.parent_object_id = OBJECT_ID({0})
                ORDER BY T.constraint_column_id
            ";
            sql = string.Format(sql, Parser.QuoteSqlStr("[" + this.SchemaName + "].[" + this.TableName + "]"));

            var tableForeignKeyDatas = da.GetDataTable(sql);
            foreach (DataRow dr in tableForeignKeyDatas.Rows)
            {
                var foreignKeyName = Helper.Tostring(dr["foreignkeyname"]);
               
                TableForeignKey existForeignKey = null;
                if (tableForeignKeys.Count > 0)
                    existForeignKey = tableForeignKeys.First(v => v.IndexName == foreignKeyName);
                if (existForeignKey == null)
                {
                    var referencedTableName = Helper.Tostring(dr["referenced_table_name"]);
                    //执行删除时为此 FOREIGN KEY 声明的引用操作。0 = 不执行任何操作 1 = 级联 2 = 设置 Null  3 = 设置默认值
                    var deleteReferentialAction = Helper.ToInt32(dr["delete_referential_action"]);
                    var delAction = ForeignKeyDelOrUpdateOperateType.NoAction;
                    if (deleteReferentialAction == 1)
                        delAction = ForeignKeyDelOrUpdateOperateType.Cascade;
                    else if (deleteReferentialAction == 2)
                        delAction = ForeignKeyDelOrUpdateOperateType.SetNull;
                    else if (deleteReferentialAction == 3)
                        delAction = ForeignKeyDelOrUpdateOperateType.SetDefault;

                    //执行更新时为此 FOREIGN KEY 声明的引用操作。 0 = 不执行任何操作 1 = 级联 2 = 设置 Null 3 = 设置默认值
                    var updateReferentialAction = Helper.ToInt32(dr["update_referential_action"]);
                    var updateAction = ForeignKeyDelOrUpdateOperateType.NoAction;
                    if (updateReferentialAction == 1)
                        updateAction = ForeignKeyDelOrUpdateOperateType.Cascade;
                    else if (updateReferentialAction == 2)
                        updateAction = ForeignKeyDelOrUpdateOperateType.SetNull;
                    else if (updateReferentialAction == 3)
                        updateAction = ForeignKeyDelOrUpdateOperateType.SetDefault;

                    tableForeignKeys.Add(new TableForeignKey()
                    {
                        IndexName = foreignKeyName,
                        ReferenceTable = referencedTableName,
                        DeleteAction = delAction,
                        UpdateAction = updateAction,
                        Columns = new List<GroupColum>(),
                        ReferenceColumns = new List<GroupColum>()
                    });
                }
                   

                existForeignKey = tableForeignKeys.First(v => v.IndexName == foreignKeyName);
                //添加外键关联的列信息

                var parentColumnName = Helper.Tostring(dr["parent_column_name"]);
                var referencedColumnName = Helper.Tostring(dr["referenced_column_name"]);
                existForeignKey.Columns.Add(new GroupColum() {
                    Name = parentColumnName
                });
                existForeignKey.ReferenceColumns.Add(new GroupColum()
                {
                    Name = referencedColumnName
                });
            }
            return tableForeignKeys;
        }

        /// <summary>
        /// 查询表索引信息
        /// </summary>
        /// <param name="da"></param>
        /// <returns></returns>
        private List<TableIndexProperty> GetTableIndexsV(DataAccess da)
        {
            //查询表的所有索引信息
            var sql = @"
                    SELECT 
	                t.index_id--索引id 0 = 堆 1 = 聚集索引 >1 = 非聚集索引
	                ,t.type --索引的类型：
	                ,t.type_desc
	                ,t.is_unique
	                ,t.name AS index_name  --索引名称
	                ,COL_NAME(t1.object_id,t1.column_id) AS column_name  --索引列名
	                ,t1.index_column_id  --索引列id
	                ,t1.key_ordinal
	                ,t1.partition_ordinal
	                ,t1.is_included_column  --0列不是包含列 1 包含列
	                ,t1.is_descending_key -- 0升序排列，1 降序排列
	                FROM sys.indexes t  
	                JOIN sys.index_columns t1
	                ON t.object_id = t1.object_id AND t.index_id = t1.index_id  
	                WHERE t.object_id =OBJECT_ID({0})
                ";
            sql = string.Format(sql, Parser.QuoteSqlStr("[" + this.SchemaName + "].[" + this.TableName + "]"));
            var tableIndexs = ObjectConvert.Convert<TableIndexProperty>(da.GetDataTable(sql)).ToList();
            return tableIndexs;
        }

        private List<TableIndex> GetTableIndexs(DataAccess da)
        {
            List<TableIndex> tableIndexs = new List<TableIndex>();
            //查询表的所有索引信息
            var sql = @"
                    --查询索引信息
					EXEC sp_helpindex @OBJNAME={0}
                ";
            sql = string.Format(sql, Parser.QuoteSqlStr("[" + this.SchemaName + "].[" + this.TableName + "]"));
            var tableIndexDatas = da.GetDataTable(sql);
            foreach (DataRow dr in tableIndexDatas.Rows)
            {
                var indexName = Helper.Tostring(dr["index_name"]);
                //索引说明，其中包括索引所在的文件组。(clustered, unique, primary key located on PRIMARY)
                var indexDescription = Helper.Tostring(dr["index_description"]); 
                //被降序索引的列将在结果集中列出，该列的名称后面带有一个减号 (-)，当列出被升序索引的列（这是默认情况）时，只带有该列的名称。
                var indexKeys = Helper.Tostring(dr["index_keys"]);

                TableIndex index = new TableIndex();
                index.IndexName = indexName;
                index.Columns = new List<GroupColum>();
                //解析索引说明
                var descs = indexDescription.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
                List<string> indexDescs = new List<string>();
                descs.ForEach(v=>{
                    if (v.IndexOf("located on") > -1)
                        v = v.Split(new string[] { "located on" },StringSplitOptions.RemoveEmptyEntries)[0];
                    v = v.Trim();
                    indexDescs.Add(v);
                });
                if (indexDescs.Contains("primary key"))
                    continue; //主键不记录到索引内
                if (indexDescs.Contains(IndexType.UNIQUE.ToString().ToLower()))
                    index.IndexType = IndexType.UNIQUE;
                else if (indexDescs.Contains(IndexType.NONCLUSTERED.ToString().ToLower()))
                    index.IndexType = IndexType.NONCLUSTERED;
                else if(indexDescs.Contains(IndexType.CLUSTERED.ToString().ToLower()))
                    index.IndexType = IndexType.CLUSTERED;

                //解析索引列信息
                var columns = indexKeys.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).ToList();
                foreach (var col in columns)
                {
                    var colname = col;
                    var isAsc = true; //是否升序
                    if (colname.IndexOf("(-)") > -1)
                    {
                        isAsc = false;
                        colname = colname.Replace("(-)","");
                    }
                    colname = colname.Trim();
                    index.Columns.Add(new GroupColum() {
                        Asc = isAsc,
                        Name = colname
                    });
                }
                tableIndexs.Add(index);
            }
            return tableIndexs;
        }
        /// <summary>
        /// 查询表所有的列信息
        /// </summary>
        /// <param name="da"></param>
        private List<TableColumnProperty> GetTableColumns(DataAccess da)
        {
            //查询表的所有列信息
            var sql = @"
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
                where t.object_id = OBJECT_ID({0})
                ";
            sql = string.Format(sql, Parser.QuoteSqlStr("[" + this.SchemaName + "].[" + this.TableName + "]"));
            var tableColums = ObjectConvert.Convert<TableColumnProperty>(da.GetDataTable(sql)).ToList();
            return tableColums;
        }
        #endregion 
    }

    /// <summary>
    /// 表索引
    /// </summary>
    public class TableIndex
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


    public class TableIndexProperty
    {
        /// <summary>
        ///  索引的 ID。 index_id仅在对象中是唯一的。
        ///  0 = 堆 1 = 聚集索引 > 1 = 非聚集索引
        /// </summary>
        public int index_id { get; set; }
        /// <summary>
        /// 索引的类型：
        /*
            0 = 堆
            1 = 聚集
            2 = 非聚集
            3 = XML
            4 = 空间
            5 = 聚集列存储索引。 适用于：SQL Server 2014 (12.x) 及更高版本。
            6 = 非聚集列存储索引。 适用于：SQL Server 2012 (11.x) 及更高版本。
            7 = 非聚集哈希索引。 适用于：SQL Server 2014 (12.x) 及更高版本。
        */
        /// </summary>
	    public int type{get;set;}
        /// <summary>
        /// 索引类型的说明
        /// </summary>
	    public string type_desc { get; set; }
        /// <summary>
        /// 1 = 索引是唯一的。0 = 索引不是唯一的。 对于聚集列存储索引始终为 0。
        /// </summary>
        public bool is_unique { get; set; }
        /// <summary>
        /// 索引名称
        /// </summary>
        public string index_name { get; set; }
        /// <summary>
        /// 索引列名
        /// </summary>
        public string column_name { get; set; }
        /// <summary>
        /// 索引列id
        /// </summary>
        public int index_column_id { get; set; }
        /// <summary>
        /*
        1 = 列是使用 CREATE INDEX INCLUDE 子句添加到索引的非键列，或者列是列存储索引的一部分。
        0 = 列不是包含列。
        因为列是聚集键的一部分而隐式添加的列未列在index_columns中。
        由于是分区列而隐式添加的列作为 0 返回。
        */
        /// </summary>
        public bool is_included_column { get; set; }
        /// <summary>
        /// 1 = 索引键列采用降序排序。 0 = 索引键列的排序方向为升序，或者列是列存储或哈希索引的一部分。
        /// </summary>
        public bool is_descending_key { get; set; }
    }
}
