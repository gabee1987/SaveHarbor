namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

public sealed partial class SupabaseRest
{
    public async Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<List<GroupSummaryDto>>("rest/v1/group_summaries?select=id,name,created_at,member_count,my_role&order=created_at.asc", cancellationToken);

    public Task<GroupSummaryDto> CreateGroupAsync(string name, string displayName, CancellationToken cancellationToken = default) =>
        RpcAsync<GroupSummaryDto>("create_group", new { p_name = name, p_display_name = displayName }, cancellationToken);

    public Task<GroupSummaryDto> JoinGroupAsync(string inviteCode, string displayName, CancellationToken cancellationToken = default) =>
        RpcAsync<GroupSummaryDto>("join_group", new { p_invite_code = inviteCode, p_display_name = displayName }, cancellationToken);

    public Task LeaveGroupAsync(string groupId, CancellationToken cancellationToken = default) =>
        auth.WithAccessTokenAsync(
            (token, callToken) => http.RpcAsync("leave_group", new { p_group = RequireUuid(groupId) }, token, callToken),
            cancellationToken);

    public Task<string> GetInviteCodeAsync(string groupId, CancellationToken cancellationToken = default) =>
        RpcAsync<string>("get_invite_code", new { p_group = RequireUuid(groupId) }, cancellationToken);

    public Task<string> RotateInviteCodeAsync(string groupId, CancellationToken cancellationToken = default) =>
        RpcAsync<string>("rotate_invite_code", new { p_group = RequireUuid(groupId) }, cancellationToken);
}
