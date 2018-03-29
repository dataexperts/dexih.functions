﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using System.Data;
using dexih.functions;
using System.Data.Common;
using System.Threading;
using static Dexih.Utils.DataType.DataType;
using dexih.functions.Query;
using dexih.transforms;
using dexih.transforms.Exceptions;

namespace dexih.connections.sql
{
    [Connection(
        ConnectionCategory = EConnectionCategory.SqlDatabase,
        Name = "PostgreSQL", 
        Description = "PostgreSQL (Postgres), is an object-relational database management system (ORDBMS) with an emphasis on extensibility and standards compliance",
        DatabaseDescription = "Database Name",
        ServerDescription = "Server:Port Name",
        AllowsConnectionString = true,
        AllowsSql = true,
        AllowsFlatFiles = false,
        AllowsManagedConnection = true,
        AllowsSourceConnection = true,
        AllowsTargetConnection = true,
        AllowsUserPassword = true,
        AllowsWindowsAuth = true,
        RequiresDatabase = true,
        RequiresLocalStorage = false
    )]
    public class ConnectionPostgreSql : ConnectionSql
    {

        public override string ServerHelp => "Server:Port Name";
        public override string DefaultDatabaseHelp => "Database";
        public override bool AllowNtAuth => true;
        public override bool AllowUserPass => true;
        public override string DatabaseTypeName => "PostgreSQL";
        public override EConnectionCategory DatabaseConnectionCategory => EConnectionCategory.SqlDatabase;

        // postgre doesn't have unsigned values, so convert the unsigned to signed 
        public override object ConvertParameterType(object value)
        {
            switch (value)
            {
                case ushort uint16:
                    return (int)uint16;
                case uint uint32:
                    return (long)uint32;
                case ulong uint64:
                    return (long)uint64;
                case null:
                    return DBNull.Value;
                default:
                    return value;
            }
        }

        public override object GetConnectionMaxValue(ETypeCode typeCode, int length = 0)
        {
            switch (typeCode)
            {
                case ETypeCode.UInt64:
                    return (ulong)long.MaxValue;
                case ETypeCode.DateTime:
                    return new DateTime(9999, 12, 31, 23, 59, 59, 999);
                default:
                    return Dexih.Utils.DataType.DataType.GetDataTypeMaxValue(typeCode, length);
            }
        }

        public override async Task<bool> TableExists(Table table, CancellationToken cancellationToken)
        {
            try
            {
                using (var connection = await NewConnection())
                using (var cmd = CreateCommand(connection, "select table_name from information_schema.tables where table_name = @NAME"))
                {
                    cmd.Parameters.Add(CreateParameter(cmd, "@NAME", table.Name));

                    var tableExists = await cmd.ExecuteScalarAsync(cancellationToken);
                    return tableExists != null;
                }
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Table exists for {table.Name} failed. {ex.Message}", ex);
            }
        }

        /// <summary>
        /// This creates a table in a managed database.  Only works with tables containing a surrogate key.
        /// </summary>
        /// <returns></returns>
        public override async Task CreateTable(Table table, bool dropTable, CancellationToken cancellationToken)
        {
            try
            {
                var tableExists = await TableExists(table, cancellationToken);

                //if table exists, and the dropTable flag is set to false, then error.
                if (tableExists && dropTable == false)
                {
                    throw new ConnectionException("The table already exists on the database.  Drop the table first.");
                }

                //if table exists, then drop it.
                if (tableExists)
                {
                    var dropResult = await DropTable(table);
                }

                var createSql = new StringBuilder();

                //Create the table
                createSql.Append("create table " + AddDelimiter(table.Name) + " ( ");
                foreach (var col in table.Columns)
                {
                    if (col.DeltaType == TableColumn.EDeltaType.AutoIncrement)
                        createSql.Append(AddDelimiter(col.Name) + " SERIAL"); //TODO autoincrement for postgresql
                    else
                    {
                        createSql.Append(AddDelimiter(col.Name) + " " + GetSqlType(col));
                        if (col.DeltaType == TableColumn.EDeltaType.AutoIncrement)
                            createSql.Append(" IDENTITY(1,1)"); //TODO autoincrement for postgresql
                        if (col.AllowDbNull == false)
                            createSql.Append(" NOT NULL");
                        else
                            createSql.Append(" NULL");
                    }
                    createSql.Append(",");
                }

                //Add the primary key using surrogate key or autoincrement.
                var key = table.GetDeltaColumn(TableColumn.EDeltaType.SurrogateKey);
                if (key == null)
                {
                    key = table.GetDeltaColumn(TableColumn.EDeltaType.AutoIncrement);
                }

                if (key != null)
                    createSql.Append("CONSTRAINT \"PK_" + AddEscape(table.Name) + "\" PRIMARY KEY (" + AddDelimiter(key.Name) + "),");


                //remove the last comma
                createSql.Remove(createSql.Length - 1, 1);
                createSql.Append(")");

                using (var connection = await NewConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = createSql.ToString();
                    try
                    {
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        throw new ConnectionException($"The sql query failed [{command.CommandText}].  {ex.Message}", ex);
                    }
                }

                return;

            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Create table for {table.Name} failed. {ex.Message}", ex);
            }
        }

        protected override string GetSqlType(TableColumn column)
        {
            string sqlType;

            switch (column.DataType)
            {
                case ETypeCode.Int32:
                case ETypeCode.UInt16:
                    sqlType = "int";
                    break;
                case ETypeCode.Byte:
                case ETypeCode.Int16:
                case ETypeCode.SByte:
                    sqlType = "smallint";
                    break;
                case ETypeCode.Int64:
                case ETypeCode.UInt32:
                case ETypeCode.UInt64:
                    sqlType = "bigint";
                    break;
                case ETypeCode.String:
                    if (column.MaxLength == null)
                        sqlType = (column.IsUnicode == true ? "n" : "") + "varchar(10485760)";
                    else
                        sqlType = (column.IsUnicode == true ? "n" : "") + "varchar(" + column.MaxLength + ")";
                    break;
				case ETypeCode.Text:
                case ETypeCode.Json:
                case ETypeCode.Xml:
                    sqlType = (column.IsUnicode == true ? "n" : "") + "text";
					break;
                case ETypeCode.Single:
                    sqlType = "real";
                    break;
                case ETypeCode.Double:
                    sqlType = "double precision";
                    break;
                case ETypeCode.Boolean:
                    sqlType = "bool";
                    break;
                case ETypeCode.DateTime:
                    sqlType = "timestamp";
                    break;
                case ETypeCode.Time:
                    sqlType = "time";
                    break;
                case ETypeCode.Guid:
                    sqlType = "text";
                    break;
                case ETypeCode.Binary:
                    sqlType = "bytea";
                    break;
                //case TypeCode.TimeSpan:
                //    SQLType = "time(7)";
                //    break;
                case ETypeCode.Unknown:
                    sqlType = "varchar(10485760)";
                    break;
                case ETypeCode.Decimal:
                    sqlType =  $"numeric ({column.Precision??28}, {column.Scale??0})";
                    break;
                default:
                    throw new Exception($"The datatype {column.DataType} is not compatible with the create table.");
            }

            return sqlType;
        }


        /// <summary>
        /// Gets the start quote to go around the values in sql insert statement based in the column type.
        /// </summary>
        /// <returns></returns>
        protected override string GetSqlFieldValueQuote(ETypeCode type, object value)
        {
            string returnValue;

            if (value == null || value is DBNull)
                return "null";

            //if (value is string && type != ETypeCode.String && string.IsNullOrWhiteSpace((string)value))
            //    return "null";

            switch (type)
            {
                case ETypeCode.Byte:
                case ETypeCode.Single:
                case ETypeCode.Int16:
                case ETypeCode.Int32:
                case ETypeCode.Int64:
                case ETypeCode.SByte:
                case ETypeCode.UInt16:
                case ETypeCode.UInt32:
                case ETypeCode.UInt64:
                case ETypeCode.Double:
                case ETypeCode.Decimal:
                    returnValue = AddEscape(value.ToString());
                    break;
                case ETypeCode.String:
				case ETypeCode.Text:
                case ETypeCode.Json:
                case ETypeCode.Xml:
                case ETypeCode.Guid:
                case ETypeCode.Boolean:
                case ETypeCode.Unknown:
                    returnValue = "'" + AddEscape(value.ToString()) + "'";
                    break;
                case ETypeCode.DateTime:
                    if (value is DateTime)
                        returnValue = "to_timestamp('" + AddEscape(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.ff")) + "', 'YYYY-MM-DD HH24:MI:SS')";
                    else
                        returnValue = "to_timestamp('" + AddEscape((string)value) + "', 'YYYY-MM-DD HH24:MI:SS')";
                    break;
                case ETypeCode.Time:
                    if (value is TimeSpan)
                        returnValue = "to_timestamp('" + AddEscape(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.ff")) + "', 'YYYY-MM-DD HH24:MI:SS')";
                    else
                        returnValue = "to_timestamp('" + AddEscape((string)value) + "', 'YYYY-MM-DD HH24:MI:SS')";
                    break;
                default:
                    throw new Exception("The datatype " + type.ToString() + " is not compatible with the sql insert statement.");
            }

            return returnValue;
        }

        public override async Task<DbConnection> NewConnection()
        {
            NpgsqlConnection connection = null;

            try
            {
                string connectionString;
                if (UseConnectionString)
                    connectionString = ConnectionString;
                else
                {
                    var hostport = Server.Split(':');
                    string port;
                    var host = hostport[0];
                    if (hostport.Count() == 1)
                    {
                        port = "5432";
                    } else
                    {
                        port = hostport[1];
                    }

                    if (UseWindowsAuth == false)
                        connectionString = "Host=" + host + "; Port=" + port + "; User Id=" + Username + "; Password=" + Password + "; ";
                    else
                        connectionString = "Host=" + host + "; Port=" + port + "; Integrated Security=true; ";

                    if (!string.IsNullOrEmpty(DefaultDatabase))
                    {
                        connectionString += "Database = " + DefaultDatabase;
                    }
                }

                connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                State = (EConnectionState)connection.State;

                if (connection.State != ConnectionState.Open)
                {
                    connection.Dispose();
                    throw new ConnectionException($"Postgre connection status is {connection.State}.");

                }

                return connection;
            }
            catch (Exception ex)
            {
                if (connection != null)
                    connection.Dispose();
                throw new ConnectionException($"Postgre connection failed. {ex.Message}", ex);
            }
        }

        public override async Task CreateDatabase(string databaseName, CancellationToken cancellationToken)
        {
            try
            {
                DefaultDatabase = "";
                using (var connection = await NewConnection())
                using (var cmd = CreateCommand(connection, "create database " + AddDelimiter(databaseName)))
                {
                    var value = await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                DefaultDatabase = databaseName;

                return;
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Create database {databaseName} failed. {ex.Message}", ex);
            }
        }

        public override async Task<List<string>> GetDatabaseList(CancellationToken cancellationToken)
        {
            try
            {
                var list = new List<string>();

                using (var connection = await NewConnection())
                using (var cmd = CreateCommand(connection, "SELECT datname FROM pg_database WHERE datistemplate = false order by datname"))
                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        list.Add((string)reader["datname"]);
                    }
                }
                return list;
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Get database list failed. {ex.Message}", ex);
            }
        }

        public override async Task<List<Table>> GetTableList(CancellationToken cancellationToken)
        {
            try
            {
                var tableList = new List<Table>();

                using (var connection = await NewConnection())
                {

                    using (var cmd = CreateCommand(connection, "select table_catalog, table_schema, table_name from information_schema.tables where table_schema not in ('pg_catalog', 'information_schema')"))
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var table = new Table()
                            {
                                Name = reader["table_name"].ToString(),
                                Schema = reader["table_schema"].ToString(),
                            };
                            tableList.Add(table); ;
                        }
                    }

                }
                return tableList;
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Get table list failed. {ex.Message}", ex);
            }
        }

        public override async Task<Table> GetSourceTableInfo(Table originalTable, CancellationToken cancellationToken)
        {
            try
            {
                if (originalTable.UseQuery)
                {
                    return await GetQueryTable(originalTable, cancellationToken);
                }

                var schema = string.IsNullOrEmpty(originalTable.Schema) ? "public" : originalTable.Schema;
                var table = new Table(originalTable.Name, originalTable.Schema);

                using (var connection = await NewConnection())
                {

                    //The new datatable that will contain the table schema
                    table.Columns.Clear();

                    // The schema table 
                    using (var cmd = CreateCommand(connection, @"
                         select * from information_schema.columns where table_schema = '" + schema + "' and table_name = '" + table.Name + "'"
                            ))
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {

                        //for the logical, just trim out any "
                        table.LogicalName = table.Name.Replace("\"", "");

                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var col = new TableColumn();

                            //add the basic properties
                            col.Name = reader["column_name"].ToString();
                            col.LogicalName = reader["column_name"].ToString();
                            col.IsInput = false;
                            col.DataType = ConvertSqlToTypeCode(reader["data_type"].ToString());
                            if (col.DataType == ETypeCode.Unknown)
                            {
                                col.DeltaType = TableColumn.EDeltaType.IgnoreField;
                            }
                            else
                            {
                                //add the primary key
                                //if (Convert.ToBoolean(reader["PrimaryKey"]) == true)
                                //    col.DeltaType = TableColumn.EDeltaType.NaturalKey;
                                //else
                                col.DeltaType = TableColumn.EDeltaType.TrackingField;
                            }

                            if (col.DataType == ETypeCode.String)
                            {
                                col.MaxLength = ConvertNullableToInt(reader["character_maximum_length"]);
                            }
                            else if (col.DataType == ETypeCode.Double || col.DataType == ETypeCode.Decimal)
                            {
                                col.Precision = ConvertNullableToInt(reader["numeric_precision_radix"]);
                                col.Scale = ConvertNullableToInt(reader["numeric_scale"]);
                            }

                            //make anything with a large string unlimited.  This will be created as varchar(max)
                            if (col.MaxLength > 4000)
                                col.MaxLength = null;

                            //col.Description = reader["Description"].ToString();
                            col.AllowDbNull = Convert.ToBoolean((string)reader["is_nullable"] == "YES");
                            //col.IsUnique = Convert.ToBoolean(reader["IsUnique"]);


                            table.Columns.Add(col);
                        }
                    }
                }
                return table;
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Get source table information for {originalTable.Name} failed. {ex.Message}", ex);
            }
        }

        private int? ConvertNullableToInt(object value)
        {
            if (value == null || value is DBNull)
            {
                return null;
            }
            else
            {
                var parsed = int.TryParse(value.ToString(), out var result);
                if (parsed)
                {
                    return result;
                }
                else
                {
                    return null;
                }
            }
        }


        public ETypeCode ConvertSqlToTypeCode(string sqlType)
        {
            switch (sqlType)
            {
                case "bit": return ETypeCode.Boolean;
                case "varbit": return ETypeCode.Binary;
                case "bytea": return ETypeCode.Binary;

                case "smallint": return ETypeCode.Int16;
                case "int": return ETypeCode.Int32;
                case "integer": return ETypeCode.Int32;
                case "bigint": return ETypeCode.Int64;
                case "smallserial": return ETypeCode.Int16;
                case "serial": return ETypeCode.Int32;
                case "bigserial": return ETypeCode.Int64;
                case "numeric": return ETypeCode.Decimal;
                case "double precision": return ETypeCode.Double;
                case "real": return ETypeCode.Double;
                case "money": return ETypeCode.Decimal;
                case "bool": return ETypeCode.Boolean;
                case "boolean": return ETypeCode.Boolean;
                case "date": return ETypeCode.DateTime;
                case "timestamp": return ETypeCode.DateTime;
                case "timestamp without time zone": return ETypeCode.DateTime;
                case "timestamp with time zone": return ETypeCode.DateTime;
                case "interval": return ETypeCode.Time;
                case "time": return ETypeCode.Time;
                case "time without time zone": return ETypeCode.Time;
                case "time with time zone": return ETypeCode.Time;
                case "character varying": return ETypeCode.String;
                case "varchar": return ETypeCode.String;
                case "character": return ETypeCode.String;
                case "text": return ETypeCode.Text;
            }
            return ETypeCode.Unknown;
        }

        public override async Task TruncateTable(Table table, CancellationToken cancellationToken)
        {
            try
            {
                using (var connection = await NewConnection())
                using (var cmd = connection.CreateCommand())
                {

                    cmd.CommandText = "truncate table " + AddDelimiter(table.Name);

                    try
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (Exception)
                    {
                        // if truncate fails, try a delete from
                        cmd.CommandText = "delete from " + AddDelimiter(table.Name);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Truncate table {table.Name} failed. {ex.Message}", ex);
            }
        }

        public override async Task<long> ExecuteInsert(Table table, List<InsertQuery> queries, CancellationToken cancellationToken)
        {
            try
            {
                var autoIncrementSql = "";
                var deltaColumn = table.GetDeltaColumn(TableColumn.EDeltaType.AutoIncrement);
                if (deltaColumn != null)
                {
                    autoIncrementSql = "SELECT max(" + AddDelimiter(deltaColumn.Name) + ") from " + AddDelimiter(table.Name);
                }

                long identityValue = 0;

                using (var connection = await NewConnection())
                {
                    var insert = new StringBuilder();
                    var values = new StringBuilder();

                    using (var transaction = connection.BeginTransaction())
                    {
                        foreach (var query in queries)
                        {
                            insert.Clear();
                            values.Clear();

                            insert.Append("INSERT INTO " + AddDelimiter(table.Name) + " (");
                            values.Append("VALUES (");

                            for (var i = 0; i < query.InsertColumns.Count; i++)
                            {
                                insert.Append(AddDelimiter(query.InsertColumns[i].Column.Name) + ",");
                                values.Append("@col" + i.ToString() + ",");
                            }

                            var insertCommand = insert.Remove(insert.Length - 1, 1).ToString() + ") " +
                                values.Remove(values.Length - 1, 1).ToString() + "); " + autoIncrementSql;

                            try
                            {
                                using (var cmd = connection.CreateCommand())
                                {
                                    cmd.CommandText = insertCommand;
                                    cmd.Transaction = transaction;

                                    for (var i = 0; i < query.InsertColumns.Count; i++)
                                    {
                                        var param = cmd.CreateParameter();
                                        param.ParameterName = "@col" + i.ToString();
                                        param.Value = query.InsertColumns[i].Value == null ? DBNull.Value : query.InsertColumns[i].Value;
                                        cmd.Parameters.Add(param);
                                    }

                                    var identity = await cmd.ExecuteScalarAsync(cancellationToken);
                                    identityValue = Convert.ToInt64(identity);

                                    cancellationToken.ThrowIfCancellationRequested();
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new ConnectionException($"Insert query failed.  {ex.Message}", ex);
                            }
                        }
                        transaction.Commit();
                    }

                    return identityValue; //sometimes reader returns -1, when we want this to be error condition.

                }
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Insert into table {table.Name} failed. {ex.Message}", ex);
            }

        }

        public static NpgsqlTypes.NpgsqlDbType GetSqlDbType(ETypeCode typeCode)
        {
            switch (typeCode)
            {
                case ETypeCode.Byte:
                    return NpgsqlTypes.NpgsqlDbType.Smallint;
                case ETypeCode.SByte:
                    return NpgsqlTypes.NpgsqlDbType.Smallint;
                case ETypeCode.UInt16:
                    return NpgsqlTypes.NpgsqlDbType.Integer;
                case ETypeCode.UInt32:
                    return NpgsqlTypes.NpgsqlDbType.Bigint;
                case ETypeCode.UInt64:
                    return NpgsqlTypes.NpgsqlDbType.Bigint;
                case ETypeCode.Int16:
                    return NpgsqlTypes.NpgsqlDbType.Smallint;
                case ETypeCode.Int32:
                    return NpgsqlTypes.NpgsqlDbType.Integer;
                case ETypeCode.Int64:
                    return NpgsqlTypes.NpgsqlDbType.Bigint;
                case ETypeCode.Decimal:
                    return NpgsqlTypes.NpgsqlDbType.Numeric;
                case ETypeCode.Double:
                    return NpgsqlTypes.NpgsqlDbType.Double;
                case ETypeCode.Single:
                    return NpgsqlTypes.NpgsqlDbType.Double;
                case ETypeCode.String:
                    return NpgsqlTypes.NpgsqlDbType.Varchar;
				case ETypeCode.Text:
					return NpgsqlTypes.NpgsqlDbType.Text;
                case ETypeCode.Boolean:
                    return NpgsqlTypes.NpgsqlDbType.Boolean;
                case ETypeCode.DateTime:
                    return NpgsqlTypes.NpgsqlDbType.Timestamp;
                case ETypeCode.Time:
                    return NpgsqlTypes.NpgsqlDbType.Time;
                case ETypeCode.Guid:
                    return NpgsqlTypes.NpgsqlDbType.Varchar;
                case ETypeCode.Binary:
                    return NpgsqlTypes.NpgsqlDbType.Bytea;
                default:
                    return NpgsqlTypes.NpgsqlDbType.Varchar;
            }
        }

        public override async Task ExecuteUpdate(Table table, List<UpdateQuery> queries, CancellationToken cancellationToken)
        {
            try
            {
                using (var connection = (NpgsqlConnection) await NewConnection())
                {

                    var sql = new StringBuilder();

                    var rows = 0;

                    using (var transaction = connection.BeginTransaction())
                    {
                        foreach (var query in queries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            sql.Clear();

                            sql.Append("update " + AddDelimiter(table.Name) + " set ");

                            var count = 0;
                            foreach (var column in query.UpdateColumns)
                            {
                                sql.Append(AddDelimiter(column.Column.Name) + " = @col" + count.ToString() + ","); // cstr(count)" + GetSqlFieldValueQuote(column.Column.DataType, column.Value) + ",");
                                count++;
                            }
                            sql.Remove(sql.Length - 1, 1); //remove last comma
                            sql.Append(" " + BuildFiltersString(query.Filters) + ";");

                            //  Retrieving schema for columns from a single table
                            using (var cmd = connection.CreateCommand())
                            {
                                cmd.Transaction = transaction;
                                cmd.CommandText = sql.ToString();

                                var parameters = new NpgsqlParameter[query.UpdateColumns.Count];
                                for (var i = 0; i < query.UpdateColumns.Count; i++)
                                {
                                    var param = cmd.CreateParameter();
                                    param.ParameterName = "@col" + i.ToString();
                                    param.NpgsqlDbType = GetSqlDbType(query.UpdateColumns[i].Column.DataType);
                                    param.Size = -1;
                                    param.Value = query.UpdateColumns[i].Value == null ? DBNull.Value : query.UpdateColumns[i].Value;
                                    cmd.Parameters.Add(param);
                                    parameters[i] = param;
                                }

                                try
                                {
                                    rows += await cmd.ExecuteNonQueryAsync(cancellationToken);
                                }
                                catch (Exception ex)
                                {
                                    throw new ConnectionException($"The update query failed.  {ex.Message}");
                                }
                            }
                        }
                        transaction.Commit();
                    }
				}
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Update table {table.Name} failed. {ex.Message}", ex);
            }
        }
    }
}
