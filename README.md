# EasyOpenVRActionInput

Unity向けのSteamVR Action Inputを扱う比較的薄いラッパーです。 
  
SteamVR Unity PluginのInput機能に依存せず、OpenVRをほぼ直接操作するため、  
VRオーバーレイツールや、非VRアプリケーションでも利用することができます。
  
OpenVRと座標系の変換にはSteamVR Unity Pluginを使用しています。  
  
使い方はInputTestScript.csを参照してください。  
ほとんどの関数はEasyOpenVRUtilのものと同じです。  
  
These codes are licensed under CC0.  
http://creativecommons.org/publicdomain/zero/1.0/deed.ja  
  
※今後、コードの構造が大きく変わる場合があります。  
  
## メモ
HMDがスリープ状態に入ると、コントローラの姿勢が取得できなくなる。(ボタンは取れる)  
EasyOpenVRUtil互換機能では取得できているため、Action Inputの制約かもしれない。  
  
## 解説
Steam VR Input System(OpenVR Action Input)についてのメモ   
https://qiita.com/gpsnmeajp/items/e423c699dde7aecb25cc  
  
SteamVR Input - ValveSoftware/openvr  
https://github.com/ValveSoftware/openvr/wiki/SteamVR-Input  
  
## Legacy Input
旧来のDevice Indexを用いた取扱用のライブラリはこちら  
  
EasyOpenVRUtil  
https://github.com/gpsnmeajp/EasyOpenVRUtil  
  
## 関連
SteamVR Unity Plugin  
https://github.com/ValveSoftware/steamvr_unity_plugin  
