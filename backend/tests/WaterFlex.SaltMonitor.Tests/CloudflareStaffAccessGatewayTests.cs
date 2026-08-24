using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Worker;
using Xunit;

namespace WaterFlex.SaltMonitor.Tests;

public sealed class CloudflareStaffAccessGatewayTests
{
    [Fact]
    public async Task Synchronize_ReplacesStaleMembershipInBothExactEmailGroups()
    {
        var handler = new RecordingHandler();
        var gateway = new CloudflareStaffAccessGateway(new HttpClient(handler), Options.Create(new StaffProvisioningOptions
        {
            CloudflareAccountId = "account", CloudflareApiToken = "token",
            CloudflarePrivilegedGroupId = "privileged", CloudflareDealerGroupId = "dealer"
        }));

        await gateway.SynchronizeAsync(["ADMIN@EXAMPLE.TEST"], ["TECH@EXAMPLE.TEST"], CancellationToken.None);

        Assert.Equal(2, handler.PutBodies.Count);
        Assert.Contains("ADMIN@EXAMPLE.TEST", handler.PutBodies[0]);
        Assert.DoesNotContain("stale@example.test", handler.PutBodies[0]);
        Assert.Contains("TECH@EXAMPLE.TEST", handler.PutBodies[1]);
        Assert.DoesNotContain("stale@example.test", handler.PutBodies[1]);
    }

    [Fact]
    public async Task Synchronize_UsesNonMatchingSentinelWhenAccessTierHasNoMembers()
    {
        var handler = new RecordingHandler();
        var gateway = new CloudflareStaffAccessGateway(new HttpClient(handler), Options.Create(new StaffProvisioningOptions
        {
            CloudflareAccountId = "account", CloudflareApiToken = "token",
            CloudflarePrivilegedGroupId = "privileged", CloudflareDealerGroupId = "dealer"
        }));

        await gateway.SynchronizeAsync(["ADMIN@EXAMPLE.TEST"], [], CancellationToken.None);

        Assert.Contains("unassigned-waterflex-access@invalid.waterflex.local", handler.PutBodies[1]);
        Assert.DoesNotContain("stale@example.test", handler.PutBodies[1]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> PutBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/access/groups", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true,"result":[{"id":"privileged","name":"WaterFlex-Privileged"},{"id":"dealer","name":"WaterFlex-Dealer"}]}""", Encoding.UTF8, "application/json")
                };
            }
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true,"result":{"name":"WaterFlex","include":[{"email":{"email":"stale@example.test"}}],"exclude":[],"require":[]}}""", Encoding.UTF8, "application/json")
                };
            }
            PutBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true}") };
        }
    }
}
