select
	'public '
+	case c.system_type_id
		when 127 then 'long'
		when 52 then 'short'
		when 56 then 'int'
		when 167 then 'string'
		when 231 then 'string'
		when 104 then 'bool'
		when 60 then 'decimal'
		when 106 then 'decimal'
		when 108 then 'decimal'
		when 58 then 'DateTime'
		when 61 then 'DateTime'
		else 'TYPE'
	end
+	case when c.is_nullable = 1 and c.system_type_id not in (167,231) then '?' else '' end
+	' '
+	c.[name]
+	' { get; set; }'
,	c.*
from sys.columns c
where c.object_id = (select object_id from sys.tables where name = 'UploadRow')
order by c.column_id
