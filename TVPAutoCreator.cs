/*
What if Dapper automatically created user-defined-table-types on-the-fly for your table-valued-parameters?

You can now do this: .AsAutoTVP()
And your Dapper query will automatically create a UDTT to match your TVP if one doesn’t already exist. And it should just magically work.
For all of those data manager functions you’ve got that just look like “insert [columns] into [table] select [columns] from [TVP]” and it’s a pain to keep the C#, procedure, and user-defined table type all in sync… And then you end up creating a type like dbo.IntIntStringBigintTwoBitsAndAPartridgeInAPearTree…

I intend on eventually submitting a pull request to Dapper for this, but I wrote it against an older version and want some feedback before I got figure out how to merge it into the current version.
*/

internal static class TVPAutoCreator
{
	public static HashSet<string> ParameterSQLTypesCreated = new HashSet<string>();

	public static string GetTVPTypeName(Type t)
	{
		return "TVPAutoCreator_" + t.FullName.Replace('.', '_');
	}

	public static void EnsureTypes(object parameters, Repository repo, string connectionString)
	{
		if (parameters != null)
		{
			var pType = parameters.GetType();
			var key = pType.FullName + " " + connectionString;
			if (!ParameterSQLTypesCreated.Contains(key))
			{
				var acTvps = pType.GetProperties().Where(p => p.PropertyType == typeof(TVPWrapper))
					.Select(tvp =>
					{
						var val = tvp.GetValue(parameters) as TVPWrapper;
						if (val != null && val.AutoCreateType && val.GetType().GenericTypeArguments.Any())
							return val;
						else
							return null;
					})
					.Where(x => x != null);

				if (acTvps.Any())
				{
					var sql = new StringBuilder();
					foreach (var val in acTvps)
					{
						var typeName = GetTVPTypeName(val.GetType().GenericTypeArguments[0]);
						var columns = val.GetType().GenericTypeArguments[0].GetProperties().Where(c => c.CustomAttributes == null || !c.CustomAttributes.Any(a => a.AttributeType == typeof(IgnoreAttribute)));

						sql.AppendFormat("if exists (select 1 from sys.types (nolock) where name = '{0}') ", typeName).AppendLine();
						sql.AppendLine("begin");
						sql.AppendFormat("drop type {0};", typeName).AppendLine();
						sql.AppendLine("end");
						sql.AppendFormat("create type {0} as table (", typeName).AppendLine();
						sql.AppendLine(string.Join(", ", columns.Select(c => c.Name + " " + GetSqlColumnType(c))));
						sql.AppendLine(");");
					}
					repo.Execute(sql.ToString());
				}

				ParameterSQLTypesCreated.Add(key);
			}
		}
	}

	public static string GetSqlColumnType(PropertyInfo p)
	{
		if (p.PropertyType == typeof(string))
			return "NVarChar(max)";
		else if (p.PropertyType == typeof(Int64) || p.PropertyType == typeof(UInt32))
			return "bigint";
		else if (p.PropertyType == typeof(UInt64))
			return "decimal(28,0)";
		else if (p.PropertyType == typeof(Int32) || p.PropertyType == typeof(UInt16) || p.PropertyType.BaseType == typeof(Enum))
			return "int";
		else if (p.PropertyType == typeof(Int16))
			return "smallint";
		else if (p.PropertyType == typeof(SByte) || p.PropertyType == typeof(Byte))
			return "tinyint";
		else if (p.PropertyType == typeof(Double))
			return "double";
		else if (p.PropertyType == typeof(float))
			return "float";
		else if (p.PropertyType == typeof(bool))
			return "bit";
		else if (p.PropertyType == typeof(Decimal))
			return "decimal(28,5)";

		throw new NotSupportedException("Column type not currently supported in TVPs: " + p.PropertyType.FullName);
	}
}


public abstract class TVPWrapper
{
	public string TypeName { get; set; }
	public bool AutoCreateType { get; set; }
	public abstract void AddParameter(IDbCommand command, string name);

	internal abstract void SetupParameter(SqlParameter parameter, string name);
}

public sealed class TVPWrapper<T> : TVPWrapper
{
	public IEnumerable<T> Values { get; set; }

	public override void AddParameter(IDbCommand command, string name)
	{
		SqlCommand sqlCmd = command as SqlCommand;
		if (sqlCmd == null)
		{
			throw new InvalidOperationException("Table-valued params are only supported by SQL Server");
		}
		var param = sqlCmd.CreateParameter();

		SetupParameter(param, name);

		sqlCmd.Parameters.Add(param);
	}

	internal override void SetupParameter(SqlParameter param, string name)
	{
		param.ParameterName = name;
		param.SqlDbType = SqlDbType.Structured;
		param.TypeName = TypeName;

		var properties = typeof(T).GetProperties();

		var datatable = new DataTable() { Locale = System.Globalization.CultureInfo.CurrentCulture };
		foreach (var prop in properties)
		{
			if (!prop.IsDefined(typeof(IgnoreAttribute), true))
			{
				var dataType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

				if (dataType.IsEnum)
					dataType = Enum.GetUnderlyingType(dataType);

				datatable.Columns.Add(prop.Name, dataType);
			}
		}

		foreach (var val in Values)
		{
			var row = datatable.NewRow();

			foreach (var prop in properties)
			{
				if (datatable.Columns.Contains(prop.Name))
				{
					row[prop.Name] = prop.GetValue(val, null) ?? DBNull.Value;
				}
			}
			datatable.Rows.Add(row);
		}

		param.Value = datatable;
	}
}


public static class TableValuedParameterExtensions
{
	public static TVPWrapper AsTableValuedParameter<T>(this IEnumerable<T> items, string typeName)
	{
		return AsTableValuedParameter(items, typeName, false);
	}
	public static TVPWrapper AsTableValuedParameter<T>(this IEnumerable<T> items, string typeName, bool autoCreateType)
	{
		return new TVPWrapper<T>
		{
			Values = items,
			TypeName = typeName,
			AutoCreateType = autoCreateType
		};
	}
	public static TVPWrapper AsAutoTVP<T>(this IEnumerable<T> items)
	{
		return AsTableValuedParameter(items, "dbo." + TVPAutoCreator.GetTVPTypeName(typeof(T)), true);
	}
}