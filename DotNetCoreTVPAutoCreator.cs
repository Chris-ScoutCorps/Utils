using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Reflection;
using System.Text;
using System.Linq;

/*

This also requires some permissions at the database level

    EXEC sp_addrolemember N'db_ddladmin', N'$(Environment)_InternalApp'
    GO
    GRANT ALTER ON SCHEMA :: udtts TO $(Environment)_InternalApp
    GO
    GRANT EXECUTE ON SCHEMA :: udtts TO $(Environment)_InternalApp
    GO

    --maybe
    DENY ALTER ON SCHEMA :: dbo TO $(Environment)_InternalApp
    GO
*/

namespace MyCompany.Common.Extensions
{
    public static class DapperExtensions
    {
        private static HashSet<string> ParameterSQLTypesCreated = new HashSet<string>();

        private static string GetTVPTypeName(Type t)
        {
            return "TVPAutoCreator_" + t.FullName.Replace('.', '_');
        }

        private static void EnsureType(Type t, SqlConnection connection)
        {
            var key = t.FullName + " " + connection.ConnectionString;
            if (!ParameterSQLTypesCreated.Contains(key))
            {
                var typeName = GetTVPTypeName(t);
                var columns = t.GetProperties().Where(c => c.CustomAttributes == null || !c.CustomAttributes.Any(a => a.AttributeType == typeof(IgnoreInTVPAttribute)));

                var sql = new StringBuilder();
                sql.AppendFormat("if exists (select 1 from sys.types t (nolock) join sys.schemas s (nolock) on s.schema_id = t.schema_id where t.name = '{0}' and s.name = 'udtts') ", typeName).AppendLine();
                sql.AppendLine("begin");
                sql.AppendFormat("drop type udtts.{0};", typeName).AppendLine();
                sql.AppendLine("end");
                sql.AppendFormat("create type udtts.{0} as table (", typeName).AppendLine();
                sql.AppendLine(string.Join(", ", columns.Select(c => c.Name + " " + GetSqlColumnType(c).Name)));
                sql.AppendLine(");");

                if (connection.State == System.Data.ConnectionState.Closed)
                    connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = sql.ToString();
                command.ExecuteNonQuery();
            }

            ParameterSQLTypesCreated.Add(key);
        }

        private class DbTypeInfo
        {
            public string Name { get; set; }
            public System.Data.SqlDbType DbType { get; set; }
            public Microsoft.SqlServer.Server.SqlMetaData Metadata { get; private set; }

            public DbTypeInfo(string name, System.Data.SqlDbType dbType, long? length = null, byte? precision = null, byte? scale = null)
            {
                Name = name;
                DbType = dbType;
                
                if (length.HasValue)
                    Metadata = new Microsoft.SqlServer.Server.SqlMetaData(Name, DbType, length.Value);
                else if (precision.HasValue)
                    Metadata = new Microsoft.SqlServer.Server.SqlMetaData(Name, DbType, precision.Value, scale.Value);
                else
                    Metadata = new Microsoft.SqlServer.Server.SqlMetaData(Name, DbType);
            }
        }

        private static Type UnwrapNullable(Type t)
        {
            var pType = t;
            if (pType.GenericTypeArguments.Any() && pType.GetGenericTypeDefinition().FullName == "System.Nullable`1")
                pType = t.GenericTypeArguments[0];
            return pType;
        }

        private static DbTypeInfo GetSqlColumnType(PropertyInfo p)
        {
            var pType = UnwrapNullable(p.PropertyType);

            if (pType == typeof(string))
                return new DbTypeInfo("NVarChar(max)", System.Data.SqlDbType.NVarChar, 4000);
            else if (pType == typeof(Int64) || pType == typeof(UInt32))
                return new DbTypeInfo("bigint", System.Data.SqlDbType.BigInt);
            else if (pType == typeof(UInt64))
                return new DbTypeInfo("decimal(28,0)", System.Data.SqlDbType.Decimal, null, 28, 0);
            else if (pType == typeof(Int32) || pType == typeof(UInt16) || pType.BaseType == typeof(Enum))
                return new DbTypeInfo("int", System.Data.SqlDbType.Int);
            else if (pType == typeof(Int16))
                return new DbTypeInfo("smallint", System.Data.SqlDbType.SmallInt);
            else if (pType == typeof(SByte) || pType == typeof(Byte))
                return new DbTypeInfo("tinyint", System.Data.SqlDbType.TinyInt);
            else if (pType == typeof(Double))
                return new DbTypeInfo("double", System.Data.SqlDbType.Float);
            else if (pType == typeof(float))
                return new DbTypeInfo("float", System.Data.SqlDbType.Float);
            else if (pType == typeof(bool))
                return new DbTypeInfo("bit", System.Data.SqlDbType.Bit);
            else if (pType == typeof(Decimal))
                return new DbTypeInfo("decimal(28,5)", System.Data.SqlDbType.Decimal, null, 28, 5);
            else if (pType == typeof(Guid))
                return new DbTypeInfo("uniqueidentifier", System.Data.SqlDbType.UniqueIdentifier);
            else if (pType == typeof(DateTime))
                return new DbTypeInfo("datetime", System.Data.SqlDbType.DateTime);

            throw new NotSupportedException("Column type not currently supported in TVPs: " + p.PropertyType.FullName);
        }


        public static void AddRecordAsDynamicTVP<T>(this SqlCommand command, string name, T row)
        {
            AddRecordsAsDynamicTVP(command, name, new[] { row });
        }

        public static void AddRecordsAsDynamicTVP<T>(this SqlCommand command, string name, IEnumerable<T> rows)
        {
            EnsureType(typeof(T), command.Connection);

            var p = command.CreateParameter();
            p.SqlDbType = System.Data.SqlDbType.Structured;
            p.ParameterName = name[0] == '@' ? name : ("@" + name);
            p.Value = rows.Select(row =>
            {
                var columns = row.GetType().GetProperties().Where(c => c.CustomAttributes == null || !c.CustomAttributes.Any(a => a.AttributeType == typeof(IgnoreInTVPAttribute))).ToArray();
                var metadata = columns.Select(c => GetSqlColumnType(c).Metadata);
                var rec = new Microsoft.SqlServer.Server.SqlDataRecord(metadata.ToArray());

                for (int i = 0; i < columns.Length; i++)
                {
                    var pType = UnwrapNullable(columns[i].PropertyType);

                    if (columns[i].GetValue(row) == null)
                        rec.SetValue(i, DBNull.Value);
                    else if (pType.BaseType == typeof(Enum))
                        rec.SetValue(i, (int) columns[i].GetValue(row));
                    else
                        rec.SetValue(i, columns[i].GetValue(row));
                }
                return rec;
            });
            p.TypeName = "udtts." + GetTVPTypeName(typeof(T));

            command.Parameters.Add(p);
        }
    }

    public class IgnoreInTVPAttribute : Attribute { }
}

//connection.Open();
//using (var com = connection.CreateCommand()) {
//com.CommandText = "insert into MyTable select * from @mytvp; select * from MyOtherTable;";
//com.AddRecordsAsDynamicTVP("mytvp", rows); //com.AddRecordAsDynamicTVP("mytvp", row);
//var reader = com.ExecuteReader();
//var data = reader.Parse<MyType>().ToArray();
//}