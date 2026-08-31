using System.Globalization;
using QQChatLocalReader.Infrastructure.Secrets;
using QQChatLocalReader.Core.Models;

namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqGroupInfoDatabaseAdapter
{
    private readonly QqPreparedDatabase database;
    private readonly QqDatabaseKey key;

    private QqGroupInfoDatabaseAdapter(QqPreparedDatabase database, QqDatabaseKey key)
    {
        this.database = database;
        this.key = key;
    }

    public static QqGroupInfoDatabaseAdapter Open(string version, QqPreparedDatabase database, QqDatabaseKey key)
    {
        if (!version.Equals(QqNtMessageDatabaseAdapter.SupportedVersion, StringComparison.Ordinal))
            throw new QqAdapterCompatibilityException("The running QQ version is not supported by the group information adapter.");
        var schema = QqDatabaseSchemaReader.Read(database, key);
        var table = schema.Tables.SingleOrDefault(item => item.Name.Equals("group_list", StringComparison.Ordinal)) ??
            throw new QqAdapterCompatibilityException("The QQ group information table is unavailable.");
        var columns = table.Columns.ToDictionary(item => item.Name, StringComparer.Ordinal);
        if (!columns.TryGetValue("60001", out var id) || !id.DeclaredType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) ||
            !columns.TryGetValue("60007", out var name) || !name.DeclaredType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
            throw new QqAdapterCompatibilityException("The QQ group information schema is unsupported.");
        var members = schema.Tables.SingleOrDefault(item => item.Name.Equals("group_member3", StringComparison.Ordinal)) ??
            throw new QqAdapterCompatibilityException("The QQ group member table is unavailable.");
        var memberColumns = members.Columns.ToDictionary(item => item.Name, StringComparer.Ordinal);
        if (!memberColumns.ContainsKey("60001") || !memberColumns.ContainsKey("1002") || !memberColumns.ContainsKey("20002"))
            throw new QqAdapterCompatibilityException("The QQ group member schema is unsupported.");
        return new QqGroupInfoDatabaseAdapter(database, key);
    }

    public IReadOnlyDictionary<string, string> ReadGroupNames()
    {
        IReadOnlyDictionary<string, string>? result = null;
        key.Use(candidate =>
        {
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"60001\", \"60007\" FROM group_list ORDER BY \"60001\";";
            using var reader = command.ExecuteReader();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                var groupId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture);
                var displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                if (displayName.Length > 0) names[groupId] = displayName;
            }

            result = names;
            return true;
        });
        return result ?? throw new InvalidOperationException("The QQ group names could not be read.");
    }

    public IReadOnlyList<GroupMemberDescriptor> ReadGroupMembers(string groupId)
    {
        if (!long.TryParse(groupId, NumberStyles.None, CultureInfo.InvariantCulture, out var numericGroupId))
            throw new ArgumentException("The group identifier is invalid.", nameof(groupId));
        IReadOnlyList<GroupMemberDescriptor>? result = null;
        key.Use(candidate =>
        {
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"1002\", \"20002\" FROM group_member3 WHERE \"60001\" = $groupId ORDER BY \"1002\";";
            command.Parameters.AddWithValue("$groupId", numericGroupId);
            using var reader = command.ExecuteReader();
            var members = new List<GroupMemberDescriptor>();
            while (reader.Read())
            {
                var memberId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture);
                var displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                members.Add(new GroupMemberDescriptor(groupId, memberId, displayName.Length == 0 ? memberId : displayName));
            }

            result = members;
            return true;
        });
        return result ?? throw new InvalidOperationException("The QQ group members could not be read.");
    }
}
