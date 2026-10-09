const unityNamespace = require("./unity-namespace.js");
const wasmSplitValues = require('./webgl-wasm-split');
const {
  launchEventType,
  scaleMode
} = require('./plugin-config.js');
require('./webgl.framework.js');
require('./plugin-config.js');

const managerConfig = {
     DATA_CDN: "",
     DATA_FILE_MD5: "928d8717082c5f98", 
     CODE_FILE_MD5: "4e8a616e7efc09de",
     GAME_NAME: "webgl",
     APPID: "tt5393252ecdf65f2907",
     DATA_FILE_SIZE: "22290716",
     OPT_DATA_FILE_SIZE: "$OPT_DATA_FILE_SIZE",
     useDataCDNAsStreamingAssetsUrl: false,
	 
     loadDataPackageFromSubpackage: true,
     compressDataPackage: true,
	   ...wasmSplitValues,

	 
     preloadDataList: [
        // 'DATA_CDN/StreamingAssets/WebGL/textures_005b9e6b32e22099edc38cba5b3d11de',
        // '/WebGL/bundles_e1af572c458eda6944e73db25cae88d5'
        ,
    ],
    
    cpJsFiles: [
      
    ],
 
     urlCacheList: [
		        "https://a.unity.cn/client_api/v1/buckets/d03196f5-b238-4ccd-9cac-f5837069ed3c/release_by_badge/latest/content/",

     ],
     dontCacheFileNames: [
		
     ]
};
GameGlobal.managerConfig = managerConfig;

function main() {
  const UnityManager = requirePlugin('UnityPlugin/index.js');
  console.log("UnityManager.version = ", UnityManager.version);
  const info = tt.getSystemInfoSync();
  const canvas = tt.createCanvas();
  canvas.width = info.screenWidth;
  canvas.height = info.screenHeight;

  Object.assign(managerConfig, {
    // callmain结束后立即隐藏封面视频
    hideAfterCallmain: true,
    
    disableLoadingPage: false,
    loadingPageConfig: {
      /**
       * !!注意：修改设计宽高和缩放模式后，需要修改文字和进度条样式。
       */
	  
      designWidth: 0,
      designHeight: 0,
      scaleMode: scaleMode.default,
      // 以下配置的样式，尺寸相对设计宽高
      textConfig: {
        firstStartText: '首次加载请耐心等待',
        downloadingText: ['正在加载资源'],
        compilingText: '编译中',
        initText: '初始化中',
        completeText: '开始游戏',
        textDuration: 1500,
        // 文字样式
        style: {
          bottom: 66,
          height: 17,
          width: 362,
          color: '#ffffff',
          fontSize: 13,
        },
      },
      // 进度条样式
      barConfig: {
        style: {
          width: 362,
          height: 12,
          padding: 2,
          bottom: 50,
          backgroundColor: '#ffffff',
        },
      },
      // 一般不修改，控制icon样式
      iconConfig: {
        visible: true,
        style: {
          width: 106,
          height: 40,
          bottom: 115,
        },
      },
      // 加载页的素材配置
      materialConfig: {
        backgroundImage: 'images/background.png',// 背景图片
        iconImage: 'images/unity_logo.png', // icon图片，一般不更换
      },
    },
  });

  const gameManager = new UnityManager(canvas, managerConfig, unityNamespace);
  gameManager.onLaunchProgress((e) => {
    // 插件加载各个阶段完成时的时机回调
    
    // interface LaunchEvent {
    //   type: LaunchEventType;
    //   data: {
    //     costTimeMs: number; // 阶段耗时
    //     runTimeMs: number; // 总耗时
    //     loadDataPackageFromSubpackage: boolean; // 首包资源是否通过小游戏分包加载
    //     isVisible: boolean; // 当前是否处于前台，onShow/onHide
    //     useCodeSplit: boolean; // 是否使用代码分包
    //     isHighPerformance: boolean; // 是否iOS高性能模式
    //     needDownloadDataPackage: boolean; // 本次启动是否需要下载资源包
    //   };
    // }
    if (e.type === launchEventType.launchPlugin) { }
    if (e.type === launchEventType.loadWasm) { }
    if (e.type === launchEventType.compileWasm) { }
    if (e.type === launchEventType.loadAssets) { }
    if (e.type === launchEventType.readAssets) { }
    if (e.type === launchEventType.prepareGame) { }
  });

  gameManager.onModulePrepared(() => {
    // unityModule has been called
  });

  gameManager.onLogError = function (err) {
    // 插件捕获到引擎错误后，通过此事件抛给游戏
    console.error(err);
  };

  globalThis.gameManager = gameManager;
  gameManager.startGame();
}

main();