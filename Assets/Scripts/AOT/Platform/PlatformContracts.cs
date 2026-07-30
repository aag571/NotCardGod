using System;
using System.Threading;
using System.Threading.Tasks;

[Serializable]
public sealed class PlatformShareData
{
    public string title;
    public string imageUrl;
    public string query;
}

[Serializable]
public sealed class RewardedAdResultData
{
    public bool completed;
    public string adSceneId;
    public string error;
}

public interface IPlatformService
{
    Task ShareAsync(PlatformShareData shareData, CancellationToken cancellationToken);
}

public interface IRewardedAdService
{
    Task<RewardedAdResultData> ShowRewardedAdAsync(string adSceneId, CancellationToken cancellationToken);
}
