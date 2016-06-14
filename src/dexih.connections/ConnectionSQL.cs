﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Data;
using dexih.functions;
using Newtonsoft.Json;
using System.IO;
using System.Data.Common;
using static dexih.functions.DataType;
using dexih.transforms;

namespace dexih.connections
{
    public class ConnectionSql : Connection
    {

        public override string ServerHelp => "Server Name";
        public override string DefaultDatabaseHelp => "Database";
        public override bool AllowNtAuth => true;
        public override bool AllowUserPass => true;
        public override bool AllowDataPoint => true;
        public override bool AllowManaged => true;
        public override bool AllowPublish => true;
        public override string DatabaseTypeName => "SQL Server";
        public override ECategory DatabaseCategory => ECategory.SqlDatabase;


        private SqlConnection _connection; //used to for the datareader function
        private SqlDataReader _sqlReader;

        public override bool CanBulkLoad => true;

        protected override async Task<ReturnValue> WriteDataBulkInner(DbDataReader reader, Table table)
        {
            try
            {
                ReturnValue<SqlConnection> connection = await NewConnection();
                if (connection.Success == false)
                {
                    return connection;
                }
                SqlBulkCopy bulkCopy = new SqlBulkCopy(connection.Value)
                {
                    DestinationTableName = DatabaseTableName(table.TableName)
                };

                await bulkCopy.WriteToServerAsync(reader);

                connection.Value.Close();

                return new ReturnValue(true, "", null);
            }
            catch (Exception ex)
            {
                return new ReturnValue(false, "The following error occurred in the bulkload processing: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Converts the table name 
        /// </summary>
        /// <returns></returns>
        public string DatabaseTableName(string tableName)
        {
            string newTableName = tableName;

            if (newTableName.Substring(0, 1) == "[")
                newTableName = newTableName.Substring(1, newTableName.Length - 2);

            return "[" + SqlEscape(newTableName) + "]";
        }

        /// <summary>
        /// This creates a table in a managed database.  Only works with tables containing a surrogate key.
        /// </summary>
        /// <returns></returns>
        public override async Task<ReturnValue> CreateManagedTable(Table table, bool dropTable = false)
        {
            try
            {
                string tableName = DatabaseTableName(table.TableName);

                ReturnValue<SqlConnection> connectionResult = await NewConnection();
                if (connectionResult.Success == false)
                {
                    return connectionResult;
                }

                SqlConnection connection = connectionResult.Value;

                SqlCommand cmd = new SqlCommand("select name from sys.tables where object_id = OBJECT_ID(@NAME)", connection);
                cmd.Parameters.Add("@NAME", SqlDbType.VarChar);
                cmd.Parameters["@NAME"].Value = tableName;

                object tableExists = null;
                try
                {
                    tableExists = await cmd.ExecuteScalarAsync();
                }
                catch (Exception ex)
                {
                    return new ReturnValue<List<string>>(false, "The sql server 'get tables' query could not be run due to the following error: " + ex.Message, ex);
                }

                if (tableExists != null && dropTable == false)
                {
                    return new ReturnValue(false, "The table " + tableName + " already exists on the underlying database.  Please drop the table first.", null);
                }

                if (tableExists != null)
                {
                    cmd = new SqlCommand("drop table " + SqlEscape(tableName), connection);
                    try
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        return new ReturnValue(false, "The following error occurred when attempting to drop the table " + tableName + ".  " + ex.Message, ex);
                    }
                }

                StringBuilder createSql = new StringBuilder();

                //Create the table
                createSql.Append("create table " + tableName + " ( ");
                foreach (TableColumn col in table.Columns)
                {
                    createSql.Append("[" + SqlEscape(col.ColumnName) + "] " + GetSqlType(col.DataType, col.MaxLength, col.Scale, col.Precision) + " ");
                    if (col.AllowDbNull == false)
                        createSql.Append("NOT NULL");
                    else
                        createSql.Append("NULL");

                    createSql.Append(",");
                }
                //remove the last comma
                createSql.Remove(createSql.Length - 1, 1);
                createSql.Append(")");

                //Add the primary key
                TableColumn key = table.GetDeltaColumn(TableColumn.EDeltaType.SurrogateKey);
                createSql.Append("ALTER TABLE " + tableName + " ADD CONSTRAINT [PK_" + tableName.Substring(1, tableName.Length - 2) + "] PRIMARY KEY CLUSTERED ([" + SqlEscape(key.ColumnName) + "])");

                cmd = connectionResult.Value.CreateCommand();
                cmd.CommandText = createSql.ToString();
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    return new ReturnValue(false, "The following error occurred when attempting to create the table " + tableName + ".  " + ex.Message, ex);
                }

                //run a query to get the schema name and also check the table has been created.
                cmd = new SqlCommand("SELECT s.name SchemaName FROM sys.tables AS t INNER JOIN sys.schemas AS s ON t.[schema_id] = s.[schema_id] where object_id = OBJECT_ID(@NAME)", connection);
                cmd.Parameters.Add("@NAME", SqlDbType.VarChar);
                cmd.Parameters["@NAME"].Value = tableName;

                object schemaName = null;
                try
                {
                    schemaName = await cmd.ExecuteScalarAsync();
                }
                catch (Exception ex)
                {
                    return new ReturnValue<List<string>>(false, "The sql server 'get tables' query could not be run due to the following error: " + ex.Message, ex);
                }

                if (schemaName == null)
                {
                    return new ReturnValue(false, "The table " + tableName + " was not correctly created.  The reason is unknown.", null);
                }

                try
                {
                    //Add the table description
                    if(!string.IsNullOrEmpty(table.Description))
                    {
                        cmd = connectionResult.Value.CreateCommand();
                        cmd.CommandText = "EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=@description , @level0type=N'SCHEMA',@level0name=@schemaname, @level1type=N'TABLE',@level1name=@tablename";
                        cmd.Parameters.AddWithValue("@description", table.Description);
                        cmd.Parameters.AddWithValue("@schemaname", schemaName);
                        cmd.Parameters.AddWithValue("@tablename", table.TableName);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    //Add the column descriptions
                    foreach (TableColumn col in table.Columns)
                    {
                        if (!string.IsNullOrEmpty(col.Description))
                        {
                            cmd = connectionResult.Value.CreateCommand();
                            cmd.CommandText = "EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=@description , @level0type=N'SCHEMA',@level0name=@schemaname, @level1type=N'TABLE',@level1name=@tablename, @level2type=N'COLUMN',@level2name=@columnname";
                            cmd.Parameters.AddWithValue("@description", col.Description);
                            cmd.Parameters.AddWithValue("@schemaname", schemaName);
                            cmd.Parameters.AddWithValue("@tablename", table.TableName);
                            cmd.Parameters.AddWithValue("@columnname", col.ColumnName);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch(Exception ex)
                {
                    return new ReturnValue(false, "The table " + tableName + " encountered an error when adding table/column descriptions: " + ex.Message, ex);
                }

                connectionResult.Value.Close();

                return new ReturnValue(true, "", null);
            }
            catch (Exception ex)
            {
                return new ReturnValue(false, "An error occurred creating the table " + table.TableName + ".  " + ex.Message, ex);
            }
        }

        /// <summary>
        /// This will add any escape charaters to sql name or value to ensure sql injection is avoided.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public string SqlEscape(string value)
        {
            return value.Replace("'", "''");
        }

        private string GetSqlType(ETypeCode dataType, int? length, int? scale, int? precision)
        {
            string sqlType;

            switch (dataType)
            {
                case ETypeCode.Int32:
                    sqlType = "int";
                    break;
                case ETypeCode.Byte:
                    sqlType = "tinyint";
                    break;
                case ETypeCode.Int16:
                    sqlType = "smallint";
                    break;
                case ETypeCode.Int64:
                    sqlType = "bigint";
                    break;
                case ETypeCode.String:
                    if(length == null)
                        sqlType = "nvarchar(max)";
                    else
                        sqlType = "nvarchar(" + length.ToString() + ")";
                    break;
                case ETypeCode.Double:
                    sqlType = "float";
                    break;
                case ETypeCode.Boolean:
                    sqlType = "bit";
                    break;
                case ETypeCode.DateTime:
                    sqlType = "datetime";
                    break;
                case ETypeCode.Time:
                    sqlType = "time(7)";
                    break;
                //case TypeCode.TimeSpan:
                //    SQLType = "time(7)";
                //    break;
                case ETypeCode.Unknown:
                    sqlType = "nvarchar(max)";
                    break;
                case ETypeCode.Decimal:
                    if (precision.ToString() == "" || scale.ToString() == "")
                        sqlType = "decimal";
                    else
                        sqlType = "decimal (" + precision.ToString() + "," + scale.ToString() + ")";
                    break;
                default:
                    throw new Exception("The datatype " +dataType.ToString() + " is not compatible with the create table.");
            }

            return sqlType;
        }


        /// <summary>
        /// Gets the start quote to go around the values in sql insert statement based in the column type.
        /// </summary>
        /// <returns></returns>
        public string GetSqlFieldValueQuote(ETypeCode type, object value)
        {
            string returnValue;

            if (value.GetType().ToString() == "System.DBNull")
                return "null";

            switch (type)
            {
                case ETypeCode.Byte:
                case ETypeCode.Int16:
                case ETypeCode.Int32:
                case ETypeCode.Int64:
                case ETypeCode.SByte:
                case ETypeCode.UInt16:
                case ETypeCode.UInt32:
                case ETypeCode.UInt64:
                case ETypeCode.Double:
                case ETypeCode.Decimal:
                    returnValue = SqlEscape(value.ToString());
                    break;
                case ETypeCode.String:
                case ETypeCode.Boolean:
                case ETypeCode.Unknown:
                    returnValue = "'" + SqlEscape(value.ToString()) + "'";
                    break;
                case ETypeCode.DateTime:
                    if(value is DateTime)
                        returnValue = "convert(datetime, '" + SqlEscape(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.ff")) + "')";
                    else
                        returnValue = "convert(datetime, '" + SqlEscape((string)value) + "')";
                    break;
                case ETypeCode.Time:
                    if (value is TimeSpan)
                        returnValue = "convert(time, '" + SqlEscape(((TimeSpan)value).ToString("HH:mm:ss.ff")) + "')";
                    else
                        returnValue = "convert(time, '" + SqlEscape((string)value) + "')";
                    break;
                default:
                    throw new Exception("The datatype " + type.ToString() + " is not compatible with the create table.");
            }

            return returnValue;
        }

        public string GetSqlCompare(Filter.ECompare compare)
        {
            switch(compare)
            {
                case Filter.ECompare.EqualTo: return "="; 
                case Filter.ECompare.GreaterThan: return ">";
                case Filter.ECompare.GreaterThanEqual: return ">=";
                case Filter.ECompare.LessThan: return "<";
                case Filter.ECompare.LessThanEqual: return "<=";
                case Filter.ECompare.NotEqual: return "!=";
                default:
                    return "";
            }
        }

        private string ConnectionString
        {
            get
            {
                string con;
                if (NtAuthentication == false)
                    con = "Data Source=" + ServerName + "; User Id=" + UserName + "; Password=" + Password + ";Initial Catalog=" + DefaultDatabase;
                else
                    con = "Data Source=" + ServerName + "; Trusted_Connection=True;Initial Catalog=" + DefaultDatabase;
                return con;
            }
        }

        public override bool CanRunQueries => true;


        private async Task<ReturnValue<SqlConnection>> NewConnection()
        {
            try
            {
                SqlConnection connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();
                State = (EConnectionState)connection.State;

                if (connection.State != ConnectionState.Open)
                {
                    return new ReturnValue<SqlConnection>(false, "The sqlserver connection failed to open with a state of : " + connection.State.ToString(), null, null);
                }
                return new ReturnValue<SqlConnection>(true, "", null, connection);
            }
            catch(Exception ex)
            {
                return new ReturnValue<SqlConnection>(false, "The sqlserver connection failed with the following message: " + ex.Message, null, null);
            }
        }

        public override async Task<ReturnValue> CreateDatabase(string databaseName)
        {
            try
            {
                DefaultDatabase = "";
                ReturnValue<SqlConnection> connection = await NewConnection();

                if (connection.Success == false)
                {
                    return new ReturnValue<List<string>>(connection.Success, connection.Message, connection.Exception, null);
                }

                SqlCommand cmd = new SqlCommand("create database [" + SqlEscape(databaseName) + "]", connection.Value);
                int value = await cmd.ExecuteNonQueryAsync();

                connection.Value.Close();

                DefaultDatabase = databaseName;

                return new ReturnValue(true);
            }
            catch(Exception ex)
            {
                return new ReturnValue<List<string>>(false, "Error creating database " + DefaultDatabase + ".   " + ex.Message, ex);
            }
        }

        public override async Task<ReturnValue<List<string>>> GetDatabaseList()
        {
            try
            {
                ReturnValue<SqlConnection> connection = await NewConnection();
                if (connection.Success == false)
                {
                    return new ReturnValue<List<string>>(connection.Success, connection.Message, connection.Exception, null);
                }

                SqlCommand cmd = new SqlCommand("SELECT name FROM sys.databases where name NOT IN ('master', 'tempdb', 'model', 'msdb') order by name", connection.Value);
                SqlDataReader reader;
                try
                {
                    reader = await cmd.ExecuteReaderAsync();
                }
                catch (Exception ex)
                {
                    return new ReturnValue<List<string>>(false, "The sql server 'get databases' query could not be run due to the following error: " + ex.Message, ex);
                }

                List<string> list = new List<string>();

                while (reader.Read())
                {
                    list.Add((string)reader["name"]);
                }

                connection.Value.Close();
                return new ReturnValue<List<string>>(true, "", null,  list);
            }
            catch (Exception ex)
            {
                return new ReturnValue<List<string>>(false, "The databases could not be listed due to the following error: " + ex.Message, ex, null);
            }
        }

        public override async Task<ReturnValue<List<string>>> GetTableList()
        {
            try
            {
                ReturnValue<SqlConnection> connection = await NewConnection();
                if (connection.Success == false)
                {
                    return new ReturnValue<List<string>>(connection.Success, connection.Message, connection.Exception, null);
                }

                SqlCommand cmd = new SqlCommand("SELECT * FROM INFORMATION_SCHEMA.Tables where TABLE_TYPE='BASE TABLE' order by TABLE_NAME", connection.Value);
                SqlDataReader reader;
                try
                {
                    reader = await cmd.ExecuteReaderAsync();
                }
                catch (Exception ex)
                {
                    return new ReturnValue<List<string>>(false, "The sql server 'get tables' query could not be run due to the following error: " + ex.Message, ex);
                }

                List<string> tableList = new List<string>();

                while(reader.Read())
                {
                    tableList.Add("[" + reader["TABLE_SCHEMA"] + "].[" + reader["TABLE_NAME"] + "]");
                }

                reader.Dispose();

                connection.Value.Close();

                return new ReturnValue<List<string>>(true, "", null, tableList);
            }
            catch (Exception ex)
            {
                return new ReturnValue<List<string>>(false, "The database tables could not be listed due to the following error: " + ex.Message, ex, null);
            }
        }

        public override async Task<ReturnValue<Table>> GetSourceTableInfo(string tableName, Dictionary<string, object> Properties = null)
        {
            try
            {
                Table table = new Table(tableName);

                string dbTableName = DatabaseTableName(tableName);

                ReturnValue<SqlConnection> connection = await NewConnection();
                if (connection.Success == false)
                {
                    return new ReturnValue<Table>(connection.Success, connection.Message, connection.Exception);
                }

                SqlDataReader reader;

                // The schema table description if it exists
                SqlCommand cmd = new SqlCommand(@"select value 'Description' 
                            FROM sys.extended_properties
                            WHERE minor_id = 0 and class = 1 and name = 'MS_Description' and
                            major_id = OBJECT_ID('" + dbTableName + "')" 
                , connection.Value);

                try
                {
                    reader = await cmd.ExecuteReaderAsync();
                }
                catch (Exception ex)
                {
                    return new ReturnValue<Table>(false, "The source sqlserver table + " + dbTableName + " could have a select query run against it with the following error: " + ex.Message, ex);
                }

                if (reader.Read())
                {
                    table.Description = (string)reader["Description"];
                }
                else
                {
                    table.Description = "";
                }

                reader.Dispose();

                //The new datatable that will contain the table schema
                table.Columns.Clear();

                // The schema table 
                cmd = new SqlCommand(@"
                         SELECT c.column_id, c.name 'ColumnName', t2.Name 'DataType', c.max_length 'MaxLength', c.precision 'Precision', c.scale 'Scale', c.is_nullable 'IsNullable', ep.value 'Description',
                        case when exists(select * from sys.index_columns ic JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id where ic.object_id = c.object_id and ic.column_id = c.column_id and is_primary_key = 1) then 1 else 0 end 'PrimaryKey'
                        FROM sys.columns c
                        INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
						INNER JOIN sys.types t2 on t.system_type_id = t2.user_type_id 
                        LEFT OUTER JOIN sys.extended_properties ep ON ep.major_id = c.object_id AND ep.minor_id = c.column_id and ep.name = 'MS_Description' and ep.class = 1 
                        WHERE c.object_id = OBJECT_ID('" + dbTableName + "') "
                        , connection.Value);

                try
                {
                    reader = await cmd.ExecuteReaderAsync();
                }
                catch(Exception ex)
                {
                    return new ReturnValue<Table>(false, "The source sqlserver table + " + dbTableName + " could have a select query run against it with the following error: " + ex.Message, ex);
                }

                table.LogicalName = table.TableName;

                while (await reader.ReadAsync())
                {
                    TableColumn col = new TableColumn();

                    //add the basic properties
                    col.ColumnName = reader["ColumnName"].ToString();
                    col.LogicalName = reader["ColumnName"].ToString();
                    col.IsInput = false;
                    col.DataType = ConvertSqlToTypeCode(reader["DataType"].ToString());
                    if (col.DataType == ETypeCode.Unknown)
                    {
                        col.DeltaType = TableColumn.EDeltaType.IgnoreField;
                    }
                    else
                    {
                        //add the primary key
                        if (Convert.ToBoolean(reader["PrimaryKey"]) == true)
                            col.DeltaType = TableColumn.EDeltaType.NaturalKey;
                        else
                            col.DeltaType = TableColumn.EDeltaType.TrackingField;
                    }

                    if (col.DataType == ETypeCode.String)
                        col.MaxLength = ConvertSqlMaxLength(reader["DataType"].ToString(), Convert.ToInt32(reader["MaxLength"]));
                    else if (col.DataType == ETypeCode.Double || col.DataType == ETypeCode.Decimal )
                    {
                        col.Precision = Convert.ToInt32(reader["Precision"]);
                        if ((string)reader["DataType"] == "money" || (string)reader["DataType"] == "smallmoney") // this is required as bug in sqlschematable query for money types doesn't get proper scale.
                            col.Scale = 4;
                        else
                            col.Scale = Convert.ToInt32(reader["Scale"]);
                    }

                    //make anything with a large string unlimited.  This will be created as varchar(max)
                    if (col.MaxLength > 4000)
                        col.MaxLength = null;


                    col.Description = reader["Description"].ToString();
                    col.AllowDbNull = Convert.ToBoolean(reader["IsNullable"]);
                    //col.IsUnique = Convert.ToBoolean(reader["IsUnique"]);
                    table.Columns.Add(col);
                }

                reader.Dispose();
                connection.Value.Close();


                return new ReturnValue<Table>(true, table);
            }
            catch (Exception ex)
            {
                return new ReturnValue<Table>(false, "The source sqlserver table + " + tableName + " could not be read due to the following error: " + ex.Message, ex);
            }
        }

        private ETypeCode ConvertSqlToTypeCode(string SqlType)
        {
            switch(SqlType)
            {
                case "bigint": return ETypeCode.Int64;
                case "binary": return ETypeCode.Unknown;
                case "bit": return ETypeCode.Boolean;
                case "char": return ETypeCode.String;
                case "date": return ETypeCode.DateTime;
                case "datetime": return ETypeCode.DateTime;
                case "datetime2": return ETypeCode.DateTime;
                case "datetimeoffset": return ETypeCode.Time;
                case "decimal": return ETypeCode.Decimal;
                case "float": return ETypeCode.Double;
                case "image": return ETypeCode.Unknown;
                case "int": return ETypeCode.Int32;
                case "money": return ETypeCode.Decimal;
                case "nchar": return ETypeCode.String;
                case "ntext": return ETypeCode.String;
                case "numeric": return ETypeCode.Decimal;
                case "nvarchar": return ETypeCode.String;
                case "real": return ETypeCode.Single;
                case "rowversion": return ETypeCode.Unknown;
                case "smalldatetime": return ETypeCode.DateTime;
                case "smallint": return ETypeCode.Int16;
                case "smallmoney": return ETypeCode.Int16;
                case "text": return ETypeCode.String;
                case "time": return ETypeCode.Time;
                case "timestamp": return ETypeCode.Int64;
                case "tinyint": return ETypeCode.Byte;
                case "uniqueidentifier": return ETypeCode.String;
                case "varbinary": return ETypeCode.Unknown;
                case "varchar": return ETypeCode.String;
                case "xml": return ETypeCode.String;
            }
            return ETypeCode.Unknown;
        }

        public int? ConvertSqlMaxLength(string sqlType, int byteLength)
        {
            if (byteLength == -1)
                return null;

            switch (sqlType)
            {
                case "char":
                case "varchar": return byteLength;
                case "nchar":
                case "nvarchar": return byteLength / 2;
            }

            return null;
        }

        private string AggregateFunction(SelectColumn column)
        {
            switch (column.Aggregate)
            {
                case SelectColumn.EAggregate.None: return "[" + column.Column + "]";
                case SelectColumn.EAggregate.Sum: return "Sum([" + column.Column + "])";
                case SelectColumn.EAggregate.Average: return "Avg([" + column.Column + "])";
                case SelectColumn.EAggregate.Min: return "Min([" + column.Column + "])";
                case SelectColumn.EAggregate.Max: return "Max([" + column.Column + "])";
                case SelectColumn.EAggregate.Count: return "Count([" + column.Column + "])";
            }

            return ""; //not possible to get here.
        }


        private string BuildSelectQuery(Table table, SelectQuery query)
        {
            StringBuilder sql = new StringBuilder();

            sql.Append("select ");
            sql.Append(String.Join(",", query.Columns.Select(c=> AggregateFunction(c) ).ToArray()) + " ");
            sql.Append("from " + DatabaseTableName(table.TableName));
            sql.Append(" WITH (NOLOCK) ");
            sql.Append(BuildFiltersString(query.Filters));

            if (query.Groups?.Count > 0)
            {
                sql.Append("group by ");
                sql.Append("[" + String.Join("],[", query.Groups.Select(c=>SqlEscape(c)).ToArray()) + "] ");
            }
            if (query.Sorts?.Count > 0)
            {
                sql.Append("order by ");
                sql.Append(String.Join(",", query.Sorts.Select(c => "[" + SqlEscape(c.Column) + "] " + (c.Direction == Sort.EDirection.Descending ? " desc" : "" )).ToArray())) ;
            }

            return sql.ToString();
        }

        private string BuildFiltersString(List<Filter> filters)
        {
            if (filters == null || filters.Count == 0)
                return "";
            else
            {
                StringBuilder sql = new StringBuilder();
                sql.Append("where ");

                foreach (var filter in filters)
                {
                    if (filter.Column1 != null)
                        sql.Append(" [" + SqlEscape(filter.Column1) + "] ");
                    else
                        sql.Append(" " + GetSqlFieldValueQuote(filter.CompareDataType, filter.Value1) + " ");

                    sql.Append(GetSqlCompare(filter.Operator));

                    if (filter.Column2 != null)
                        sql.Append(" [" + SqlEscape(filter.Column2) + "] ");
                    else
                        sql.Append(" " + GetSqlFieldValueQuote(filter.CompareDataType, filter.Value2) + " ");

                    sql.Append(filter.AndOr.ToString());
                }

                sql.Remove(sql.Length - 3, 3); //remove last or/and

                return sql.ToString();
            }
        }

        protected override async Task<ReturnValue> DataReaderStartQueryInner(Table table, SelectQuery query)
        {
            if (OpenReader)
            {
                return new ReturnValue(false, "The current connection is already open.", null);
            }

            CachedTable = table;

            ReturnValue<SqlConnection> connection = await NewConnection();
            if (connection.Success == false)
            {
                return connection;
            }

            _connection = connection.Value;

            string sql = BuildSelectQuery(table, query);
            SqlCommand cmd = new SqlCommand(sql, _connection);

            try
            {
                _sqlReader = await cmd.ExecuteReaderAsync();
            }
            catch(Exception ex)
            {
                return new ReturnValue(false, "The connection reader for the sqlserver table " + table.TableName + " could failed due to the following error: " + ex.Message, ex);
            }

            if (_sqlReader == null)
            {
                return new ReturnValue(false, "The connection reader for the sqlserver table " + table.TableName + " return null for an unknown reason.  The sql command was " + sql, null);
            }
            else
            {
                OpenReader = true;
                return new ReturnValue(true, "", null);
            }
        }

        public override async Task<ReturnValue<int>> ExecuteUpdateQuery(Table table, List<UpdateQuery> queries)
        {
            ReturnValue<SqlConnection> connection = await NewConnection();
            if (connection.Success == false)
            {
                return new ReturnValue<int>(connection.Success, connection.Message, connection.Exception, -1);
            }

            StringBuilder sql = new StringBuilder();

            int rows = 0;

            using (var transaction = connection.Value.BeginTransaction())
            {
                foreach (var query in queries)
                {
                    sql.Clear();

                    sql.Append("update " + DatabaseTableName(table.TableName) + " set ");

                    foreach (QueryColumn column in query.UpdateColumns)
                        sql.Append("[" + SqlEscape(column.Column) + "] = " + GetSqlFieldValueQuote(column.ColumnType, column.Value) + ",");
                    sql.Remove(sql.Length - 1, 1); //remove last comma
                    sql.Append(" " + BuildFiltersString(query.Filters));

                    //  Retrieving schema for columns from a single table
                    SqlCommand cmd = new SqlCommand(sql.ToString(), connection.Value, transaction);

                    try
                    {
                        rows += cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        return new ReturnValue<int>(false, "The sql server update query for " + table.TableName + " could not be run due to the following error: " + ex.Message + ".  The sql command was " + sql.ToString(), ex, -1);
                    }
                }
                transaction.Commit();
            }

            connection.Value.Close();
            return new ReturnValue<int>(true, "", null, rows == -1 ? 0 : rows); //sometimes reader returns -1, when we want this to be error condition.
        }

        public override async Task<ReturnValue<int>> ExecuteDeleteQuery(Table table, List<DeleteQuery> queries)
        {
            ReturnValue<SqlConnection> connection = await NewConnection();
            if (connection.Success == false)
            {
                return new ReturnValue<int>(connection.Success, connection.Message, connection.Exception, -1);
            }

            StringBuilder sql = new StringBuilder();
            int rows = 0;

            using (var transaction = connection.Value.BeginTransaction())
            {
                foreach (var query in queries)
                {
                    sql.Clear();
                    sql.Append("delete from " + DatabaseTableName(table.TableName) + " ");
                    sql.Append(BuildFiltersString(query.Filters));

                    SqlCommand cmd = new SqlCommand(sql.ToString(), connection.Value, transaction);

                    try
                    {
                        rows += await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        return new ReturnValue<int>(false, "The sql server delete query for " + table.TableName + " could not be run due to the following error: " + ex.Message + ".  The sql command was " + sql.ToString(), ex, -1);
                    }
                }
                transaction.Commit();
            }

            connection.Value.Close();
            return new ReturnValue<int>(true, "", null, rows == -1 ? 0 : rows); //sometimes reader returns -1, when we want this to be error condition.
        }

        public override async Task<ReturnValue<int>> ExecuteInsertQuery(Table table, List<InsertQuery> queries)
        {
            ReturnValue<SqlConnection> connection = await NewConnection();
            if (connection.Success == false)
            {
                return new ReturnValue<int>(connection.Success, connection.Message, connection.Exception, -1);
            }

            StringBuilder insert = new StringBuilder();
            StringBuilder values = new StringBuilder();
            int rows = 0;

            using (var transaction = connection.Value.BeginTransaction())
            {
                foreach (var query in queries)
                {
                    insert.Clear();
                    values.Clear();

                    insert.Append("INSERT INTO " + DatabaseTableName(table.TableName) + " (");
                    values.Append("VALUES (");

                    for (int i = 0; i < query.InsertColumns.Count; i++)
                    {
                        insert.Append("[" + query.InsertColumns[i].Column + "],");
                        values.Append("@col" + i.ToString() + ",");
                    }

                    string insertCommand = insert.Remove(insert.Length - 1, 1).ToString() + ") " + values.Remove(values.Length - 1, 1).ToString() + ");";

                    try
                    {
                        using (var cmd = connection.Value.CreateCommand())
                        {
                            cmd.CommandText = insertCommand;
                            cmd.Transaction = transaction;
                            for (int i = 0; i < query.InsertColumns.Count; i++)
                            {
                                cmd.Parameters.AddWithValue("@col" + i.ToString(), query.InsertColumns[i].Value);
                            }
                            rows += cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        return new ReturnValue<int>(false, "The sql server insert query for " + table.TableName + " could not be run due to the following error: " + ex.Message + ".  The sql command was " + insertCommand?.ToString(), ex, -1);
                    }
                }
                transaction.Commit();
            }

            connection.Value.Close();
            return new ReturnValue<int>(true, "", null, rows == -1 ? 0 : rows); //sometimes reader returns -1, when we want this to be error condition.
        }

        public override async Task<ReturnValue<object>> ExecuteScalar(Table table, SelectQuery query)
        {
            ReturnValue<SqlConnection> connection = await NewConnection();
            if (connection.Success == false)
            {
                return new ReturnValue<object>(connection);
            }

            string sql = BuildSelectQuery(table, query);

            //  Retrieving schema for columns from a single table
            SqlCommand cmd = new SqlCommand(sql, connection.Value);
            object value;
            try
            {
                value = await cmd.ExecuteScalarAsync();
            }
            catch (Exception ex)
            {
                return new ReturnValue<object>(false, "The sql server select query for " + table.TableName + " could not be run due to the following error: " + ex.Message + ".  Sql command was: " + sql, ex, null);
            }

            connection.Value.Close();
            return new ReturnValue<object>(true, value); 
        }

        public override async Task<ReturnValue> TruncateTable(Table table)
        {
            ReturnValue<SqlConnection> connection = await NewConnection();
            if (connection.Success == false)
            {
                return connection;
            }

            SqlCommand cmd = new SqlCommand("truncate table " + DatabaseTableName(table.TableName), connection.Value);
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                return new ReturnValue(false, "The sql server update query for " + table.TableName + " could not be run due to the following error: " + ex.Message, ex);
            }

            connection.Value.Close();

            //if(rows == -1)
            //    return new ReturnValue(false, "The sql server truncate table query for " + Table.TableName + " could appears to have failed for an unknown reason." , null);
            //else
            return new ReturnValue(true, "", null);
        }

        public override string GetCurrentFile()
        {
            throw new NotImplementedException();
        }

         public override ReturnValue ResetTransform()
        {
            throw new NotImplementedException();
        }

        public override bool Initialize()
        {
            throw new NotImplementedException();
        }

        public override string Details()
        {
            StringBuilder details = new StringBuilder();
            details.AppendLine("<b>Source</b> <br />");
            details.AppendLine("<b>Database</b>: SQL Server<br />");
            details.AppendLine("<b>Table</b>: " + CachedTable.TableName + "<br />");
            details.AppendLine("<b>SQL</b>: " + BuildSelectQuery(CachedTable, SelectQuery));
            return details.ToString();
        }

        public override List<Sort> RequiredSortFields()
        {
            throw new NotImplementedException();
        }

        public override List<Sort> RequiredJoinSortFields()
        {
            throw new NotImplementedException();
        }


        public override async Task<ReturnValue> AddMandatoryColumns(Table table, int position)
        {
            return await Task.Run(() => new ReturnValue(true));
        }

        public override async Task<ReturnValue> DataWriterStart(Table table)
        {
            return await Task.Run(() => new ReturnValue(true));
        }

        public override async Task<ReturnValue> DataWriterFinish(Table table)
        {
            return await Task.Run(() => new ReturnValue(true));
        }

        public override bool CanLookupRowDirect { get; } = true;

        public override async Task<ReturnValue<object[]>> LookupRowDirect(List<Filter> filters)
        {
            // if nocache is selected, then query the database for the lookup row.  
            if (CacheMethod == ECacheMethod.NoCache)
            {
                ReturnValue<SqlConnection> connection = await NewConnection();
                if (connection.Success == false)
                {
                    return new ReturnValue<object[]>(connection);
                }

                SelectQuery query = new SelectQuery()
                {
                    Columns = CachedTable.Columns.Select(c => new SelectColumn(c.ColumnName)).ToList(),
                    Table = CachedTable.TableName,
                    Filters = filters
                };
                string sql = BuildSelectQuery(CachedTable, query);

                //  Retrieving schema for columns from a single table
                SqlCommand cmd = new SqlCommand(sql, connection.Value);
                DbDataReader reader;
                try
                {
                    reader = await cmd.ExecuteReaderAsync();
                }
                catch (Exception ex)
                {
                    return new ReturnValue<object[]>(false, "The sql server lookup query for " + CachedTable.TableName + " could not be run due to the following error: " + ex.Message + ".  Sql command was: " + sql, ex);
                }

                if (reader.Read())
                {
                    object[] values = new object[CachedTable.Columns.Count];
                    reader.GetValues(values);
                    return new ReturnValue<object[]>(true, values);
                }
                else
                    return new ReturnValue<object[]>(false, "The sql server lookup query for " + CachedTable.TableName + " return no rows.  Sql command was: " + sql, null);
            }
            else
                return await base.LookupRow(filters);

        }

        protected override ReturnValue<object[]> ReadRecord()
        {
            if(_sqlReader == null)
                throw new Exception("The sql server reader has not been set.");

            try
            {
                bool result = _sqlReader.Read();
                if (result == false && _connection != null && _connection.State == ConnectionState.Open)
                    _connection.Close();

                if (result)
                {
                    object[] row = new object[CachedTable.Columns.Count];
                    _sqlReader.GetValues(row);
                    return new ReturnValue<object[]>(true, row);
                }
                else
                    return new ReturnValue<object[]>(false, null);
            }
            catch(Exception ex)
            {
                throw new Exception( "The sql server reader failed due to the following error: " + ex.Message, ex);
            }
        }


    }
}
