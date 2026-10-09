const unityNamespace = {
      unityVersion: '2022.3.57f1c2', 
      assetBundleFSEnabled: true,
      assetBundleBufferCapacity: 128,
      assetBundleBufferTTL: 5,
      // DATA_CDN + dataFileSubPrefix + datafilename
      dataFileSubPrefix:'',
      optimizeWebGLMemoryInBackground: true,
      bootConfig: 'player-connection-ip=10.132.247.97',
	  UnityModule: null,
}
GameGlobal.unityNamespace = GameGlobal.unityNamespace || unityNamespace;
module.exports = unityNamespace;