﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using dexih.connections.sql;
using dexih.functions;
using dexih.functions.Query;
using dexih.transforms;
using dexih.transforms.Exceptions;
using Dexih.Utils.DataType;
using Oracle.ManagedDataAccess.Client;
using Oracle.SqlAndPlsqlParser.LocalParsing;

namespace dexih.connections.oracle
{
    [Connection(
        ConnectionCategory = EConnectionCategory.SqlDatabase,
        Name = "Oracle", 
        Description = "Oracle relational database management system (RDBMS)",
        DatabaseDescription = "Database Name",
        ServerDescription = "Oracle Server",
        ServerHelp = "Use the format <ip or hostname>:<port>/<service name>",
        AllowsConnectionString = true,
        AllowsSql = true,
        AllowsFlatFiles = false,
        AllowsManagedConnection = true,
        AllowsSourceConnection = true,
        AllowsTargetConnection = true,
        AllowsUserPassword = true,
        AllowsWindowsAuth = false,
        RequiresDatabase = true,
        RequiresLocalStorage = false
    )]
    public class ConnectionOracle : ConnectionSql
    {
        protected override string SqlDelimiterOpen { get; } = $"\"";
        protected override string SqlDelimiterClose { get; } = "\"";
        public override bool CanUseBinary => true;
        public override bool CanUseDateTimeOffset => true;

        public override bool CanUseBoolean => false;
        public override bool CanUseTimeSpan => false;
        public override bool CanUseUnsigned => false;
        public override bool CanUseSByte { get; } = false;
        
        protected override char SqlParameterIdentifier => ':';
        public override bool AllowsTruncate { get; } = false;

        
        public override object GetConnectionMaxValue(ETypeCode typeCode, int length = 0)
        {
            switch (typeCode)
            {
                case ETypeCode.DateTime:
                    return new DateTime(9999,12,31, 23, 59, 59, 0);
                case ETypeCode.Date:
                    return new DateTime(9999,12,31);
                case ETypeCode.DateTimeOffset:
                    return new DateTimeOffset(9999,12,31,0,0,0, TimeSpan.Zero);
                case ETypeCode.UInt64:
                    return (ulong)long.MaxValue;
                //TODO Oracle driver giving error when converting any numeric with scientific number 
                case ETypeCode.Double:
                    return (double)1000000000000000F;
                case ETypeCode.Single:
                    return 1E20F;
                case ETypeCode.Decimal:
                    return 1E+20m;
                default:
                    return DataType.GetDataTypeMaxValue(typeCode, length);
            }
        }
        
        public override object GetConnectionMinValue(ETypeCode typeCode, int length = 0)
        {
            switch (typeCode)
            {
                case ETypeCode.Double:
                    return (double)-1000000000000000F;
                case ETypeCode.Single:
                    return -1E20F;
                case ETypeCode.Decimal:
                    return -1E+20m;
                case ETypeCode.String:
                case ETypeCode.Text:
                    return null;
                default:
                    return DataType.GetDataTypeMinValue(typeCode, length);
            }
        }
        
        public override object ConvertForRead(DbDataReader reader, int ordinal, TableColumn column)
        {
            if (column.DataType == ETypeCode.DateTimeOffset && reader is OracleDataReader oracleDataReader)
            {
                var time = oracleDataReader.GetOracleTimeStampTZ(ordinal);
                if (time.IsNull)
                {
                    return null;
                }
                return new DateTimeOffset(time.Value, time.GetTimeZoneOffset());
            }

            return base.ConvertForRead(reader, ordinal, column);
        }
        
        protected override DbCommand CreateCommand(DbConnection connection)
        {
            var cmd = (OracleCommand) connection.CreateCommand();
            cmd.BindByName = true;
            return cmd;
        }
        
//        public override object ConvertParameterType(ETypeCode typeCode, int rank, object value)
//        {
//            if (value == null || value == DBNull.Value)
//            {
//                return DBNull.Value;
//            }
//
//            if (rank > 0 && !CanUseArray)
//            {
//                return Operations.Parse(ETypeCode.String, value);
//            }
//
//            // GUID's get parameterized as binary.  So need to explicitly convert to string.
//            if (typeCode == ETypeCode.Guid || typeCode == ETypeCode.UInt64)
//            {
//                return value.ToString();
//            }
//
//            if (typeCode == ETypeCode.Boolean)
//            {
//                return (bool) value ? 1 : 0;
//            }
//            
//            return value;
//        }
        
        public override DbParameter CreateParameter(DbCommand command, string name, ETypeCode type, int rank, ParameterDirection direction, object value)
        {
            var param = (OracleParameter) command.CreateParameter();
            param.ParameterName = name;
            param.Direction = direction;
            var converted = ConvertForWrite(param.ParameterName, type, rank, true, value);
            param.OracleDbType = GetSqlDbType(converted.typeCode);
            param.Value = converted.value;

            return param;
        }
        
        public override async Task<DbConnection> NewConnection(CancellationToken cancellationToken)
        {
            OracleConnection connection = null;

            try
            {
                string connectionString;
                if (UseConnectionString)
                    connectionString = ConnectionString;
                else
                {
                    if (string.IsNullOrEmpty(Server))
                    {
                        throw new ConnectionException("There was no server name specified for the oracle connection.");
                    }
                    
                    connectionString = $"Data Source={Server}; Connection Timeout={ConnectionTimeout}; ";

                    if (UseWindowsAuth)
                    {
                        connectionString += "; Integrated Security=yes";    
                    }
                    else
                    {
                        connectionString += $"User Id={Username}; Password={Password}; ";
                    }
                    

                    var userName = Username?.ToLower();
                    if (userName == "sys")
                    {
                        connectionString = connectionString + "; DBA Privilege=SYSDBA";
                    }
                }

                connection = new OracleConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                State = (EConnectionState)connection.State;

                if (connection.State != ConnectionState.Open)
                {
                    connection.Dispose();
                    throw new ConnectionException($"The Oracle connection has a state of {connection.State}.");
                }
                
                // set the default schema
                if (!string.IsNullOrEmpty(DefaultDatabase))
                {
                    await using (var cmd = CreateCommand(connection, $"ALTER SESSION SET CURRENT_SCHEMA={AddDelimiter(DefaultDatabase)}"))
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                }

                return connection;
            }
            catch (Exception ex)
            {
                connection?.Dispose();
                throw new ConnectionException($"Oracle connection failed. {ex.Message}", ex);
            }
        }
        
        protected override DbCommand CreateCommand(DbConnection connection, string commandText, DbTransaction transaction = null)
        {
            var cmd = new OracleCommand(commandText, (OracleConnection) connection) {BindByName = true, Transaction = (OracleTransaction) transaction};
            return cmd;
        }

     protected override string GetSqlType(TableColumn column)
        {
            string sqlType;

            if (column.Rank > 0)
            {
                return "VARCHAR(2000)";
            }

            switch (column.DataType)
            {
                case ETypeCode.Byte:
                    sqlType = "NUMBER(3, 0)";
                    break;
                case ETypeCode.SByte:
                    sqlType = "NUMBER(3, 0)";
                    break;
                case ETypeCode.UInt16:
                    sqlType = "NUMBER(6, 0)";
                    break;
				case ETypeCode.Int16:
                    sqlType = "NUMBER(5, 0)";
                    break;
                case ETypeCode.UInt32:
                    sqlType = "NUMBER(10, 0)";
                    break;
                case ETypeCode.Int32:
                    sqlType = "NUMBER(10, 0)";
                    break;
                case ETypeCode.Int64:
                    sqlType = "NUMBER(19,0)";
                    break;
				case ETypeCode.UInt64:
					sqlType = "NUMBER(19,0)";
                    break;
                case ETypeCode.String:
                    if (column.MaxLength == null)
                        sqlType = (column.IsUnicode == true ? "n" : "") +  "VARCHAR(2000)";
                    else
                        sqlType = (column.IsUnicode == true ? "n" : "") + "VARCHAR(" + column.MaxLength + ")";
                    break;
				case ETypeCode.Text:
                case ETypeCode.Json:
                case ETypeCode.Xml:
                case ETypeCode.Node:
                    sqlType = "CLOB";
					break;
                case ETypeCode.Single:
                    sqlType = "FLOAT(63)";
                    break;
                case ETypeCode.Double:
                    sqlType = "FLOAT(126)";
                    break;
                case ETypeCode.Boolean:
                    sqlType = "NUMBER(1,0)";
                    break;
                case ETypeCode.DateTime:
                case ETypeCode.Date:
                    sqlType = "DATE";
                    break;
                case ETypeCode.DateTimeOffset:
                    sqlType = "TIMESTAMP WITH TIME ZONE";
                    break;
                case ETypeCode.Time:
                    sqlType = "CHAR(40)";
                    break;
                case ETypeCode.Guid:
                    sqlType = "CHAR(36)";
                    break;
                case ETypeCode.Binary:
                    sqlType = "BLOB";
                    break;
                case ETypeCode.Unknown:
                    sqlType = "CLOB";
                    break;
                case ETypeCode.Decimal:
                    sqlType = $"NUMBER ({column.Precision??29}, {column.Scale??8})";
                    break;
                case ETypeCode.Geometry:
                    sqlType = "BLOB";
                    break;
                default:
                    throw new Exception($"The datatype {column.DataType} is not compatible with the create table.");
            }

            return sqlType;
        }
        

//        private string EscapeString(string value)
//        {
//            var chars = value.ToCharArray();
//            var sb = new StringBuilder();
//            for (var i = 0; i < chars.Length; i++)
//            {
//                switch (chars[i])
//                {
//                    case '\'':
//                        sb.Append("\'\'"); break;
//                    default:
//                        sb.Append(chars[i]); break;
//                }
//            }
//            return sb.ToString();
//        }

        /// <summary>
        /// Gets the start quote to go around the values in sql insert statement based in the column type.
        /// </summary>
        /// <returns></returns>
//        protected string GetSqlFieldValueQuote(ETypeCode typeCode, int rank, object value)
//        {
//            string returnValue;
//
//            if (value == null || value is DBNull)
//                return "null";
//
//            if (rank > 0)
//            {
//                return "'" + EscapeString(value.ToString()) + "'";
//            }
//            //if (value is string && type != ETypeCode.String && string.IsNullOrWhiteSpace((string)value))
//            //    return "null";
//
//            switch (typeCode)
//            {
//                case ETypeCode.Byte:
//                case ETypeCode.Single:
//                case ETypeCode.Int16:
//                case ETypeCode.Int32:
//                case ETypeCode.Int64:
//                case ETypeCode.SByte:
//                case ETypeCode.UInt16:
//                case ETypeCode.UInt32:
//                case ETypeCode.UInt64:
//                case ETypeCode.Double:
//                case ETypeCode.Decimal:
//                    returnValue = EscapeString(value.ToString());
//                    break;
//                case ETypeCode.String:
//				case ETypeCode.Text:
//                case ETypeCode.Json:
//                case ETypeCode.Xml:
//                case ETypeCode.Guid:
//                case ETypeCode.Unknown:
//                    returnValue = "'" + EscapeString(value.ToString()) + "'";
//                    break;
//                case ETypeCode.DateTime:
//                    if (value is DateTime)
//                        returnValue = "TO_DATE('" + EscapeString(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss")) + "', 'YYYY-MM-DD HH24:MI:SS')";
//                    else
//						returnValue = "STR_TO_DATE('"+ EscapeString((string)value) + "', 'YYYY-MM-DD HH24:MI:SS')";
//                    break;
//                case ETypeCode.Time:
//                    if (value is TimeSpan span)
//						returnValue = "'" + EscapeString(span.ToString("c")) + "'";
//					else
//                        returnValue = "'" + EscapeString((string)value) + "'";
//					break;
//                case ETypeCode.Boolean:
//                    var v = (bool) Operations.Parse(ETypeCode.Boolean, value);
//                    return v ? "1" : "0";
//                case ETypeCode.Binary:
//
//                    return "'" + Operations.Parse(ETypeCode.String, value) + "'";
//                default:
//                    throw new Exception($"The datatype {typeCode} is not compatible with the sql insert statement.");
//            }
//
//            return returnValue;
//        }

        public override async Task CreateDatabase(string databaseName, CancellationToken cancellationToken = default)
        {
            try
            {
                DefaultDatabase = "";

                await using (var connection = await NewConnection(cancellationToken))
                {
                    await using (var cmd = CreateCommand(connection,
                        $"create user {AddDelimiter(databaseName)} identified by {AddDelimiter(databaseName)} CONTAINER=CURRENT"))
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var cmd = CreateCommand(connection,
                        $"grant create session to {AddDelimiter(databaseName)}"))
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var cmd = CreateCommand(connection, $"ALTER USER {AddDelimiter(databaseName)} quota unlimited on USERS"))
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                DefaultDatabase = databaseName;
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Create database {databaseName} failed. {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// This creates a table in a managed database.  Only works with tables containing a surrogate key.
        /// </summary>
        /// <returns></returns>
        public override async Task CreateTable(Table table, bool dropTable, CancellationToken cancellationToken = default)
        {
            try
            {
                await using (var connection = await NewConnection(cancellationToken))
                {
                    var tableExists = await TableExists(table, cancellationToken);

                    //if table exists, and the dropTable flag is set to false, then error.
                    if (tableExists && dropTable == false)
                    {
                        throw new ConnectionException($"The table {table.Name} already exists on {Name} and the drop table option is set to false.");
                    }

                    //if table exists, then drop it.
                    if (tableExists)
                    {
                        await DropTable(table, cancellationToken);
                    }

                    var createSql = new StringBuilder();

                    //Create the table
                    createSql.Append("create table " + SqlTableName(table) + " ");

                    //add comments
//                    if (!string.IsNullOrEmpty(table.Description))
//                        createSql.Append(" -- " + table.Description);

                    createSql.AppendLine("");
                    createSql.Append("(");

                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        var col = table.Columns[i];

                        createSql.Append(AddDelimiter(col.Name) + " " + GetSqlType(col) + " ");
                        
                        if (col.DeltaType == EDeltaType.DbAutoIncrement)
                            createSql.Append("GENERATED ALWAYS as IDENTITY(START with 1 INCREMENT by 1) ");

                        if (col.AllowDbNull == false)
                            createSql.Append("NOT NULL ");
                        else
                            createSql.Append("NULL ");
                        
                        if (i < table.Columns.Count - 1)
                            createSql.Append(",");

//                        if (!string.IsNullOrEmpty(col.Description))
//                            createSql.Append(" -- " + col.Description);

                        createSql.AppendLine();
                    }

                    createSql.AppendLine(")");

                    await using (var command = connection.CreateCommand())
                    {
                        try
                        {
                            command.CommandText = createSql.ToString();
                            await command.ExecuteNonQueryAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            throw new ConnectionException($"Create table failed: {ex.Message}, sql command: {createSql}.", ex);
                        }
                    }

                    var skCol = table.GetAutoIncrementColumn();
                    if (skCol != null)
                    {
                        await using (var command = connection.CreateCommand())
                        {
                            command.CommandText = $"alter table {SqlTableName(table)} add (constraint {table.Name +"_pk"} primary key ( {AddDelimiter(skCol.Name)} ))";
                            await command.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Create table {table.Name} failed.  {ex.Message}", ex);
            }
        }

        public override async Task<List<string>> GetDatabaseList(CancellationToken cancellationToken = default)
        {
            try
            {
                var list = new List<string>();

                await using (var connection = await NewConnection(cancellationToken))
                await using (var cmd = CreateCommand(connection, "select USERNAME from USER_USERS"))
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        list.Add((string)reader["USERNAME"]);
                    }
                }
                return list;
            }
            catch (Exception ex)
            {
                throw new ConnectionException($"Get schema list failed. {ex.Message}", ex);
            }
        }
        
        public override async Task<List<Table>> GetTableList(CancellationToken cancellationToken = default)
        {
            try
            {
                var tableList = new List<Table>();

                await using (var connection = await NewConnection(cancellationToken))
                {
                    await using (var cmd = CreateCommand(connection,
                        "select object_name from USER_OBJECTS where object_type = 'TABLE'"))
                    {
                        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var table = new Table
                                {
                                    Name = reader[0].ToString(),
                                    Schema =  DefaultDatabase
                                };
                                tableList.Add(table);
                            }
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
        
        private int? ConvertNullableToInt(object value)
        {
            if(value == null || value is DBNull)
            {
                return null;
            }
            else 
            {
                var parsed = int.TryParse(value.ToString(), out var result);
                if(parsed) 
                {
                    return result;
                }
                else 
                {
                    return null;
                }
            }
        }
        
        public ETypeCode ConvertSqlToTypeCode(string sqlType, int? precision, int? scale)
        {
            switch (sqlType.ToLower())
            {
                case "number":
                    if (precision > 0) return ETypeCode.Decimal;
                    if (scale < 5) return ETypeCode.Int16;
                    if (scale < 10) return ETypeCode.Int32;
                    return ETypeCode.Int64;
                case "integer": 
                    return  ETypeCode.Int32;
                case "smallint": 
                    return  ETypeCode.Int16;				       
                case "numeric": 
                case "decimal": 
                    return ETypeCode.Decimal;
                case "float":
                    return ETypeCode.Double;
                case "date":
                case "timestamp":
                    return ETypeCode.DateTime;
                case "timestamp with timezone":
                    return ETypeCode.DateTimeOffset;
                case "char": 
                case "nchar": 
                case "varchar": 
                case "nvarchar": 
                case "varchar2": 
                case "nvarchar2": 
                case "rowid": 
                    return ETypeCode.String;
                case "long":
                case "clob":
                case "nclob":
                    return ETypeCode.Text;
                case "bfile": 
                case "blob": 
                case "raw": 
                    return ETypeCode.Binary;
            }
            return ETypeCode.Unknown;
        }
        
        public override async Task<Table> GetSourceTableInfo(Table originalTable, CancellationToken cancellationToken = default)
        {
            if (originalTable.TableType == Table.ETableType.Query)
            {
                return await GetQueryTable(originalTable, cancellationToken);
            }
            
            try
            {
				var schema = string.IsNullOrEmpty(originalTable.Schema) ? "public" : originalTable.Schema;
                var table = new Table(originalTable.Name, originalTable.Schema);

                await using (var connection = await NewConnection(cancellationToken))
                {
                    // table table comment if exists
                    await using (var cmd = CreateCommand(connection,
                        @"SELECT comments FROM all_tab_comments WHERE OWNER = '" + schema + "' AND TABLE_NAME='" + table.Name +
                        "'"))
                    {
                        var comment = await cmd.ExecuteScalarAsync(cancellationToken);
                        if (comment != null)
                        {
                            table.Description = comment.ToString();
                        }
                    }

                    //The new datatable that will contain the table schema
                    table.Columns.Clear();

                    // The schema table 
                    await using (var cmd = CreateCommand(connection, @"SELECT * FROM all_tab_columns WHERE OWNER = '" + schema + "' AND TABLE_NAME='" + table.Name + "'"))
                    await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var name = reader["COLUMN_NAME"].ToString();
                            var dataType = reader["DATA_TYPE"].ToString();
                            var maxLength = ConvertNullableToInt(reader["DATA_LENGTH"]);
                            var precision = ConvertNullableToInt(reader["DATA_PRECISION"]);
                            var scale = ConvertNullableToInt(reader["DATA_SCALE"]);
                            
                            var col = new TableColumn
                            {
                                Name = name,
                                LogicalName = name,
                                IsInput = false,
                                DataType = ConvertSqlToTypeCode(dataType, precision, scale),
                                AllowDbNull = reader["NULLABLE"].ToString() != "N" 
                            };

                            if (col.DataType == ETypeCode.Unknown)
                            {
                                col.DeltaType = EDeltaType.IgnoreField;
                            }
                            else
                            {
                                col.DeltaType = EDeltaType.TrackingField;
                            }

							switch (col.DataType)
							{
							    case ETypeCode.String:
							        col.MaxLength = maxLength;
							        break;
							    case ETypeCode.Double:
							    case ETypeCode.Decimal:
							        col.Precision = precision;
							        col.Scale = scale;
							        break;
							}

                            //make anything with a large string unlimited.  This will be created as varchar(max)
                            if (col.MaxLength > 4000)
                                col.MaxLength = null;

                            // col.Description = reader["COLUMN_COMMENT"].ToString();
                            //col.IsUnique = Convert.ToBoolean(reader["IsUnique"]);


                            table.Columns.Add(col);
                        }
                    }
                    
                    // add any column comments
                    await using (var cmd = CreateCommand(connection, $@"select COLUMN_NAME, COMMENTS from all_col_comments where table_name= '{table.Name}' and owner = '{table.Schema}'"))
                    await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var name = reader["COLUMN_NAME"].ToString();
                            var column = table.Columns[name];
                            if (column != null)
                            {
                                column.Description = reader["COMMENTS"].ToString();
                            }
                        }
                    }
                    
                    // get the primary key
                    await using (var cmd = CreateCommand(connection, $@"
SELECT cols.table_name, cols.column_name, cols.position, cons.status, cons.owner
FROM all_constraints cons, all_cons_columns cols
WHERE cols.table_name = '{table.Name}'
AND cols.owner = '{table.Schema}'
AND cons.constraint_type = 'P'
AND cons.constraint_name = cols.constraint_name
AND cons.owner = cols.owner
ORDER BY cols.table_name, cols.position"))
                    await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var name = reader["COLUMN_NAME"].ToString();
                            var column = table.Columns[name];
                            if (column != null)
                            {
                                column.DeltaType = EDeltaType.NaturalKey;
                            }
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

        private OracleDbType GetSqlDbType(ETypeCode typeCode)
        {
            return typeCode switch
            {
                ETypeCode.Byte => OracleDbType.Byte,
                ETypeCode.SByte => OracleDbType.Int16,
                ETypeCode.UInt16 => OracleDbType.Int32,
                ETypeCode.UInt32 => OracleDbType.Int64,
                ETypeCode.UInt64 => OracleDbType.Int64,
                ETypeCode.Int16 => OracleDbType.Int16,
                ETypeCode.Int32 => OracleDbType.Int32,
                ETypeCode.Int64 => OracleDbType.Int64,
                ETypeCode.Decimal => OracleDbType.Decimal,
                ETypeCode.Double => OracleDbType.Double,
                ETypeCode.Single => OracleDbType.Double,
                ETypeCode.String => OracleDbType.Varchar2,
                ETypeCode.Text => OracleDbType.Clob,
                ETypeCode.Json => OracleDbType.Clob,
                ETypeCode.Xml => OracleDbType.Clob,
                ETypeCode.Node => OracleDbType.Clob,
                ETypeCode.Unknown => OracleDbType.Clob,
                ETypeCode.Boolean => OracleDbType.Byte,
                ETypeCode.DateTime => OracleDbType.Date,
                ETypeCode.Date => OracleDbType.Date,
                ETypeCode.DateTimeOffset => OracleDbType.TimeStampTZ,
                ETypeCode.Time => OracleDbType.Varchar2,
                ETypeCode.Guid => OracleDbType.Varchar2,
                ETypeCode.Binary => OracleDbType.Blob,
                _ => OracleDbType.Varchar2
            };
        }
        
//          public override async Task ExecuteInsertBulk(Table table, DbDataReader reader, CancellationToken cancellationToken = default)
//          {
//              MaxSqlSize = 40000;
//            try
//            {
//                var timer = Stopwatch.StartNew();
//                using (var connection = await NewConnection())
//                {
//                    var fieldCount = reader.FieldCount;
//                    var row = new StringBuilder();
//                    
//                    var columns = table.Columns.Where(c => c.DeltaType != EDeltaType.AutoIncrement).ToArray();
//                    var ordinals = new int[columns.Length];
//                    
//                    for(var i = 0; i< columns.Length; i++)
//                    {
//                        ordinals[i] = reader.GetOrdinal(columns[i].Name);
//                    }
//
//                    while (!reader.IsClosed || row.Length > 0)
//                    {
//                        var insert = new StringBuilder();
//
//                        // build an sql command that looks like
//                        // INSERT ALL
//                        //     INTO User (FirstName, LastName) VALUES ('gary','holland'),('jack','doe')
//                        //     INTO User (FirstName, LastName) VALUES ('gary','holland'),('jack','doe')
//                        //     INTO User (FirstName, LastName) VALUES ('gary','holland'),('jack','doe')
//                        // select 1 from dual
//                        insert.Append("INSERT ALL " );
//
//                        var isFirstRow = true;
//                        
//                        // if there is a cached row from previous loop, add it to the sql.
//                        if (row.Length > 0)
//                        {
//                            insert.Append(row);
//                            row.Clear();
//                            isFirstRow = false;
//                        }
//
//                        while (await reader.ReadAsync(cancellationToken))
//                        {
//                            row.AppendLine(" INTO " + SqlTableName(table) + " (" + string.Join(',', columns.Select(c => AddDelimiter(c.Name))) + ")");
//                            row.AppendLine(" values ");
//
//                            row.Append("(");
//                            row.Append(string.Join(',', columns.Select((c, i) => GetSqlFieldValueQuote(c.DataType, c.Rank, reader[ordinals[i]]))));
//                            row.Append(")");
//
//                            // if the maximum sql size will be exceeded with this value, then break, so the command can be executed.
//                            if (insert.Length + row.Length + 2 > MaxSqlSize)
//                                break;
//
//                            // if(!isFirstRow) insert.Append(",");
//                            insert.Append(row);
//                            row.Clear();
//                            isFirstRow = false;
//                        }
//
//                        if (!isFirstRow)
//                        {
//                            // sql statement is going to be too large to handle, so exit.
//                            if (insert.Length > MaxSqlSize)
//                            {
//                                throw new ConnectionException($"The generated sql was too large to execute.  The size was {(insert.Length + row.Length)} and the maximum supported by MySql is {MaxSqlSize}.  To fix this, either reduce the fields being used or increase the `max_allow_packet` variable in the MySql database.");
//                            }
//
//                            insert.AppendLine(" select 1 from dual");
//
//                            using (var cmd = new OracleCommand(insert.ToString(), (OracleConnection)connection))
//                            {
//                                cmd.CommandType = CommandType.Text;
//                                try
//                                {
//                                    cmd.ExecuteNonQuery();
//                                }
//                                catch (Exception ex)
//                                {
//#if DEBUG
//                                    throw new ConnectionException("Error running following sql command: " + insert.ToString(0, 500), ex);
//#else
//                                    throw new ConnectionException("Error running following sql command", ex);
//#endif                                
//                                }
//                            
//                            }
//                        }
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                throw new ConnectionException($"Bulk insert failed. {ex.Message}", ex);
//            }
//        }

        public override async Task<long> ExecuteInsert(Table table, List<InsertQuery> queries, int transactionReference, CancellationToken cancellationToken = default)
        {
             try
             {
                 if (queries.Count == 0) return 0;
                
                 long identityValue = 0;
                 long autoIncrementValue = 0;

                 var transactionConnection = await GetTransaction(transactionReference, cancellationToken);
                 var connection = (OracleConnection) transactionConnection.connection;
                 var transaction = (OracleTransaction) transactionConnection.transaction;

                 try
                 {
                     var insert = new StringBuilder();
                     var values = new StringBuilder();

                     foreach (var query in queries)
                     {
                         cancellationToken.ThrowIfCancellationRequested();

                         insert.Clear();
                         values.Clear();

                         insert.Append("INSERT INTO " + SqlTableName(table) + " (");
                         values.Append("VALUES (");

                         for (var i = 0; i < query.InsertColumns.Count; i++)
                         {
                             if (query.InsertColumns[i].Column.DeltaType == EDeltaType.DbAutoIncrement)
                                 continue;
                            
                             if (query.InsertColumns[i].Column.DeltaType == EDeltaType.AutoIncrement)
                                 autoIncrementValue = Convert.ToInt64(query.InsertColumns[i].Value);
                            
                             insert.Append(AddDelimiter(query.InsertColumns[i].Column.Name) + ",");
                             values.Append(":col" + i + " ,");
                         }

                         var insertCommand = insert.Remove(insert.Length - 1, 1) + ") " +
                                             values.Remove(values.Length - 1, 1) + ") ";

                         try
                         {
                             await using (var cmd = connection.CreateCommand())
                             {
                                 cmd.CommandText = insertCommand;
                                 cmd.Transaction = transaction;

                                 for (var i = 0; i < query.InsertColumns.Count; i++)
                                 {
                                     var insertColumn = query.InsertColumns[i];
                                     var param = CreateParameter(cmd, $"col{i}", insertColumn.Column.DataType,
                                         insertColumn.Column.Rank, ParameterDirection.Input, insertColumn.Value);
                                     cmd.Parameters.Add(param);
                                 }
                                
                                 cancellationToken.ThrowIfCancellationRequested();

                                 await cmd.ExecuteNonQueryAsync(cancellationToken);

                             }

                         }
                         catch (Exception ex)
                         {
                             throw new ConnectionException($"The insert query failed.  {ex.Message}", ex);
                         }
                     }
                    
                     if (autoIncrementValue > 0)
                     {
                         return autoIncrementValue;
                     }

                     var deltaColumn = table.GetColumn(EDeltaType.DbAutoIncrement);
                     if (deltaColumn != null)
                     {
                         var sql = $" select max({AddDelimiter(deltaColumn.Name)}) from {AddDelimiter(table.Name)}";
                         await using (var cmd = connection.CreateCommand())
                         {
                             cmd.CommandText = sql;
                             cmd.Transaction = transaction;
                             var identity = await cmd.ExecuteScalarAsync(cancellationToken);
                             identityValue = Convert.ToInt64(identity);
                         }
                     }
                    
                     return identityValue; //sometimes reader returns -1, when we want this to be error condition.
                 }
                 finally
                 {
                     EndTransaction(transactionReference, transactionConnection);
                 }
             }
             catch(Exception ex)
             {
                 throw new ConnectionException($"Insert into table {table.Name} failed. {ex.Message}", ex);
             }        
        }
        
//        public override async Task ExecuteUpdate(Table table, List<UpdateQuery> queries, int transactionReference, CancellationToken cancellationToken = default)
//        {
//            try
//            {
//                var sql = new StringBuilder();
//
//                var rows = 0;
//
//                var transactionConnection = await GetTransaction(transactionReference);
//                var connection = (OracleConnection) transactionConnection.connection;
//                var transaction = (OracleTransaction) transactionConnection.transaction;
//
//                try
//                {
//                    foreach (var query in queries)
//                    {
//                        sql.Clear();
//
//                        sql.Append("update " + SqlTableName(table) + " set ");
//
//                        var count = 0;
//                        foreach (var column in query.UpdateColumns)
//                        {
//                            sql.Append(AddDelimiter(column.Column.Name) + " = :col" + count + ",");
//                            count++;
//                        }
//
//                        sql.Remove(sql.Length - 1, 1); //remove last comma
//
//                        //  Retrieving schema for columns from a single table
//                        using (var cmd = connection.CreateCommand())
//                        {
//                            sql.Append(" " + BuildFiltersString(query.Filters, cmd));
//
//                            cmd.Transaction = transaction;
//                            cmd.CommandText = sql.ToString();
//
//                            var parameters = new DbParameter[query.UpdateColumns.Count];
//                            for (var i = 0; i < query.UpdateColumns.Count; i++)
//                            {
//                                var param = cmd.CreateParameter();
//                                param.ParameterName = ":col" + i;
//                                var convert = ConvertForWrite(query.UpdateColumns[i].Column,
//                                    query.UpdateColumns[i].Value);
//                                param.OracleDbType = GetSqlDbType(convert.typeCode);
//                                param.Value = convert.value;
//                                cmd.Parameters.Add(param);
//                                parameters[i] = param;
//                            }
//
//                            cancellationToken.ThrowIfCancellationRequested();
//
//                            try
//                            {
//                                rows += await cmd.ExecuteNonQueryAsync(cancellationToken);
//                            }
//                            catch (Exception ex)
//                            {
//                                throw new ConnectionException($"The update query failed. {ex.Message}", ex);
//                            }
//                        }
//                    }
//                }
//                finally
//                {
//                    EndTransaction(transactionReference, transactionConnection);
//                }
//            }
//
//            catch (Exception ex)
//            {
//                throw new ConnectionException($"Update table {table.Name} failed.  {ex.Message}", ex);
//            }
//        }

        public override async Task<bool> TableExists(Table table, CancellationToken cancellationToken = default)
        {
            try
            {
                await using (var connection = await NewConnection(cancellationToken))
                await using (var cmd = CreateCommand(connection, "select object_name from all_objects where object_type = 'TABLE' and OBJECT_NAME = :NAME and OWNER = :SCHEMA"))
                {
                    cmd.Parameters.Add(CreateParameter(cmd, "NAME", ETypeCode.Text, 0, ParameterDirection.Input, table.Name));
                    cmd.Parameters.Add(CreateParameter(cmd, "SCHEMA", ETypeCode.Text, 0, ParameterDirection.Input, DefaultDatabase));
                    var tableExists = await cmd.ExecuteScalarAsync(cancellationToken);
                    return tableExists != null;
                }
            }
            catch(Exception ex)
            {
                throw new ConnectionException($"Table exists failed. {ex.Message}", ex);
            }
        }
    }
}