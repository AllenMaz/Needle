using Action.Comm;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace NeedleConsole
{
    class Program
    {
        public static string dbString = "Data Source=.\\SQLENTERPRISE;Initial Catalog=PLL_ERP_Co_03;User ID=sa;Password=123456";

        static void Main(string[] args)
        {

            
            using (DataAccess da = new DataAccess(dbString))
            {
                var table = DesignTableTest1();

                table = DesignTableTest2();
                table.UpgradeTable(da);
                
            }

            Console.ReadLine();
        }

        private static TableModel DesignTableTest1()
        {
            TableModel table = new TableModel();
            table.TableName = "aaatesttable";
            table.Columns = new List<TableColumn>() {
                new TableColumn()
                {
                    Name = "Id",
                    Identity = true,
                    DBType = FieldType.Int,
                },
                new TableColumn()
                {
                    Name = "Name",
                    DBType = FieldType.Char,
                    Desc="名称",
                    Size = 10,
                },
                 new TableColumn()
                {
                    Name = "Title",
                    DBType = FieldType.BigInt,
                    Desc = "标题"
                },
                  new TableColumn()
                {
                    Name = "Code",
                    DBType = FieldType.Nvarchar,
                    Size = 20,
                },
                new TableColumn()
                {
                    Name = "Desc",
                    DBType = FieldType.Nvarchar,
                    Size = 100,
                },
                new TableColumn()
                {
                    Name = "fkey1",
                    DBType = FieldType.Varchar,
                    Size = 39,
                },
                new TableColumn()
                {
                    Name = "fkey2",
                    DBType = FieldType.Varchar,
                    Size = 20,
                },
                new TableColumn()
                {
                    Name = "Money",
                    DBType = FieldType.Decimal,
                    Precision = 18,
                    Scale = 2
                },
                new TableColumn()
                {
                    Name = "Price",
                    DBType = FieldType.Decimal,
                },
                 new TableColumn()
                {
                    Name = "Remark",
                    DBType = FieldType.Nvarchar,
                    Size = -1,
                },
                new TableColumn()
                {
                    Name = "CreateTime",
                    DBType = FieldType.DateTime
                }
            };
            //设置主键
            table.PrimaryKey = new TablePrimaryKey()
            {
                Columns = new List<GroupColum>() {
                    new GroupColum(){
                        Name = "Id",
                    }
                },
                IndexName = "PK_ID_NAME",
                IndexType = IndexType.CLUSTERED
            };
            //设置索引
            table.Indexs = new List<TableIndex>
            {
                new TableIndex()
                {
                    Columns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "Desc",
                            Asc = false
                        }
                    },
                    IndexName = "PK_unique",
                    IndexType = IndexType.UNIQUE
                },
                 new TableIndex()
                {
                    Columns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "Code",
                            Asc = false
                        },
                        new GroupColum()
                        {
                            Name = "Title",
                            Asc = false
                        }
                    },
                    IndexName = "PK_index1",
                    IndexType = IndexType.NONCLUSTERED
                },
                  new TableIndex()
                {
                    Columns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "CreateTime",
                            Asc = true
                        }
                    },
                    IndexName = "PK_index2",
                    IndexType = IndexType.NONCLUSTERED
                }
            };

            //设置外键
            table.ForeignKeys = new List<TableForeignKey>()
            {
                new TableForeignKey()
                {
                    Columns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "fkey1",
                        },
                         new GroupColum()
                        {
                            Name = "fkey2",
                        }
                    },
                    IndexName = "FK_foreign",
                    ReferenceTable = "abacustpartsel",
                    ReferenceColumns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "partno",
                        },
                         new GroupColum()
                        {
                            Name = "custno",
                        }
                    },
                    DeleteAction = ForeignKeyDelOrUpdateOperateType.No_Action,
                    UpdateAction = ForeignKeyDelOrUpdateOperateType.Cascade
                }
            };
            return table;
        }
        private static TableModel DesignTableTest2()
        {
            TableModel table = new TableModel();
            table.TableName = "aaatesttable";
            table.Columns = new List<TableColumn>() {
                new TableColumn()
                {
                    Name = "Id",
                    Identity = true,
                    DBType = FieldType.Int,
                },
                new TableColumn()
                {
                    Name = "Name",
                    DBType = FieldType.Char,
                    Desc="名称",
                    Size = 10,
                },
                 new TableColumn()
                {
                    Name = "Title",
                    DBType = FieldType.BigInt,
                    Desc = "标题",
                    DefaulValue = "10"
                },
                  new TableColumn()
                {
                    Name = "Codeaaa",
                    DBType = FieldType.Nvarchar,
                    Size = 20,
                },
                new TableColumn()
                {
                    Name = "Desc",
                    DBType = FieldType.Nvarchar,
                    Size = 500,
                },
                new TableColumn()
                {
                    Name = "Money",
                    DBType = FieldType.Decimal,
                    Precision = 18,
                    Scale = 2
                },
                new TableColumn()
                {
                    Name = "Price",
                    DBType = FieldType.Decimal,
                },
                 new TableColumn()
                {
                    Name = "Remark",
                    DBType = FieldType.Nvarchar,
                    Size =-1,
                },
                new TableColumn()
                {
                    Name = "CreateTime",
                    DBType = FieldType.DateTime
                }
            };
            //设置主键
            table.PrimaryKey = new TablePrimaryKey()
            {
                Columns = new List<GroupColum>() {
                    new GroupColum(){
                        Name = "Id",
                    }
                },
                IndexName = "PK_ID_NAME",
                IndexType = IndexType.CLUSTERED
            };
            //设置外键
            table.ForeignKeys = new List<TableForeignKey>()
            {
                new TableForeignKey()
                {
                    Columns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "Codeaaa",
                        }
                    },
                    IndexName = "FK_foreign_111",
                    ReferenceTable = "accParams",
                    ReferenceColumns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "ParamId",
                        }
                    },
                    DeleteAction = ForeignKeyDelOrUpdateOperateType.No_Action,
                    UpdateAction = ForeignKeyDelOrUpdateOperateType.Cascade
                }
            };
            //设置索引
            table.Indexs = new List<TableIndex>
            {
                new TableIndex()
                {
                    Columns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "Desc",
                            Asc = false
                        },
                        new GroupColum()
                        {
                            Name = "Codeaaa",
                            Asc = false
                        }
                    },
                    IndexName = "PK_unique",
                    IndexType = IndexType.UNIQUE
                },
                 new TableIndex()
                {
                    Columns = new List<GroupColum>()
                    {
                        new GroupColum()
                        {
                            Name = "Codeaaa",
                            Asc = false
                        }
                    },
                    IndexName = "PK_index1",
                    IndexType = IndexType.NONCLUSTERED
                }
            };

            return table;
        }

    }


}
