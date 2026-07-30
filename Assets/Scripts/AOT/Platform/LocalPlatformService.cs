using System.Threading;
using System.Threading.Tasks;

public sealed class LocalPlatformService : IPlatformService, IRewardedAdService
{
    public Task ShareAsync(PlatformShareData shareData, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<RewardedAdResultData> ShowRewardedAdAsync(string adSceneId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new RewardedAdResultData
        {
            completed = true,
            adSceneId = adSceneId
        });
    }
}
