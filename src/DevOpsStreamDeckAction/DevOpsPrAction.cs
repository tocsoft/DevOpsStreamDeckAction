using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using StreamDeckLib;
using StreamDeckLib.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsStreamDeckAction
{
    [ActionUuid(Uuid = "tocsoft.streamdeck.azuredevops.openPrs")]
    public class DevOpsPrAction : BaseStreamDeckActionWithSettingsModel<Models.DevOpsPrActionSettingsModel>
    {
        public DevOpsPrAction()
        {

        }

        CancellationTokenSource cts;
        Task t;
        private void StartMonitorPrs(string context)
        {
            cts = new CancellationTokenSource();
            var token = cts.Token;
            t = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await UpdatePRCounter(context);
                    await Task.Delay(10000, token);
                }
            }, token);
        }

        private void StopMonitorPrs(string context)
        {
            cts?.Cancel();
            t?.Wait();
            cts = null;
            t = null;
        }

        public override async Task OnKeyUp(StreamDeckEventPayload args)
        {
            if (string.IsNullOrWhiteSpace(SettingsModel.Project))
            {
                await Manager.OpenUrlAsync(args.context, $"https://dev.azure.com/{SettingsModel.Organisation}/_pulls");
            }
            else
            {
                await Manager.OpenUrlAsync(args.context, $"https://dev.azure.com/{SettingsModel.Organisation}/_git/{SettingsModel.Project}/pullrequests?_a=mine");
            }
        }

        public override async Task OnDidReceiveSettings(StreamDeckEventPayload args)
        {
            await base.OnDidReceiveSettings(args);
            await UpdatePRCounter(args.context);
        }

        public override async Task OnWillAppear(StreamDeckEventPayload args)
        {
            await base.OnWillAppear(args);
            StartMonitorPrs(args.context);
        }
        public override Task OnApplicationDidLaunch(StreamDeckEventPayload args)
        {
            return base.OnApplicationDidLaunch(args);
        }
        public override Task OnApplicationDidTerminate(StreamDeckEventPayload args)
        {
            return base.OnApplicationDidTerminate(args);
        }

        int prCount = -1;
        private async Task UpdatePRCounter(string context)
        {
            // query for the number of open PRs

            // if PR count goes up

            var collectionUri = $"https://dev.azure.com/{this.SettingsModel.Organisation}";

            VssCredentials creds = new VssBasicCredential("", SettingsModel.PersonalAccessToken);

            VssConnection connection = new VssConnection(new Uri(collectionUri), creds);
            var projectsClient = connection.GetClient<ProjectHttpClient>();
            var gitclient = connection.GetClient<GitHttpClient>();

            var profileClient = connection.GetClient<Microsoft.VisualStudio.Services.Profile.Client.ProfileHttpClient>();
            var profile = await profileClient.GetProfileAsync(new Microsoft.VisualStudio.Services.Profile.ProfileQueryContext(Microsoft.VisualStudio.Services.Profile.AttributesScope.Core)
            {

            });


            try
            {
                List<Guid> projectIds = new List<Guid>();

                if (!string.IsNullOrWhiteSpace(SettingsModel.Project))
                {
                    var project = await projectsClient.GetProject(SettingsModel.Project);
                    projectIds.Add(project.Id);
                }
                else
                {
                    var projects = await projectsClient.GetProjects(top: 100);
                    projectIds.AddRange(projects.Select(x => x.Id));
                    do
                    {
                        projects = await projectsClient.GetProjects(top: 100, skip: projectIds.Count, continuationToken: projects.ContinuationToken);
                        projectIds.AddRange(projects.Select(x => x.Id));

                    } while (projects.Count >= 100);
                }


                var t = projectIds.Select(x =>
                    gitclient.GetPullRequestsByProjectAsync(x, new GitPullRequestSearchCriteria
                    {
                        Status = PullRequestStatus.Active
                    }, top: 0)
                 );

                var allPrResults = await Task.WhenAll(t);
                var myPrs = allPrResults.SelectMany(x => x)
                    .Where(x => x.Reviewers.Any(r => r.UniqueName == profile.EmailAddress))
                    .Where(x => x.IsDraft != true);

                var n = myPrs.Count();

                if (n <= 0)
                {
                    await Manager.SetTitleAsync(context, $"");
                }
                else
                {
                    await Manager.SetTitleAsync(context, n.ToString());
                }

                if (prCount < n && prCount >= 0)
                {
                    Manager.ShowAlertAsync(context);
                }
                prCount = n;
            }
            catch
            {

            }
        }

        public override Task OnWillDisappear(StreamDeckEventPayload args)
        {
            StopMonitorPrs(args.context);
            // stop monitoring
            return base.OnWillDisappear(args);
        }
    }
}
