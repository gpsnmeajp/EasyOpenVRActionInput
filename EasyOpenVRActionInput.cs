/**
 * EasyOpenVRActionInput by gpsnmeajp v0.01
 * https://github.com/gpsnmeajp/EasyOpenVRActionInput
 * https://sabowl.sakura.ne.jp/gpsnmeajp/
 * 
 * These codes are licensed under CC0.
 * http://creativecommons.org/publicdomain/zero/1.0/deed.ja
 */

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Valve.VR;

namespace EasyLazyLibrary
{
    public class EasyOpenVRActionInput
    {
        //定数定義
        public const uint InvalidDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
        public const ulong InvalidInputHandle = 0;

        //VRハンドル
        CVRSystem openvr = null;
        //Inputハンドル
        CVRInput vrinput = null;

        //内部保持用全デバイス姿勢
        TrackedDevicePose_t[] allDevicePose;

        //デバイス姿勢を常にアップデートするか
        bool autoupdate = true;

        //光子遅延補正予測時間(0=補正なし or 予測時間取得失敗)
        float PredictedTime = 0f;

        //最終更新フレームカウント
        int LastFrameCount = 0;

        //ハンドル辞書
        Dictionary<string, ulong> ActionSetHandles = new Dictionary<string, ulong>();
        Dictionary<string, ulong> ActionHandles = new Dictionary<string, ulong>();
        Dictionary<string, ulong> InputSourceHandles = new Dictionary<string, ulong>();

        //-----------内部クラス------------------

        //姿勢クラス(EasyOpenVRUtil互換)
        public class ActionTransform
        {
            public uint deviceid = InvalidDeviceIndex;
            public Vector3 position = Vector3.zero;
            public Quaternion rotation = Quaternion.identity;
            public Vector3 velocity = Vector3.zero;
            public Vector3 angularVelocity = Vector3.zero;

            //デバッグ用
            public override string ToString()
            {
                return "deviceid: " + deviceid + " position:" + position.ToString() + " rotation:" + rotation.ToString() + " velocity:" + velocity.ToString() + " angularVelocity:" + angularVelocity.ToString();
            }
        }

        //VRActiveActionSet格納用クラス
        public class ActiveActionSets
        {
            public List<VRActiveActionSet_t> List = new List<VRActiveActionSet_t>();
            public void Add(VRActiveActionSet_t set)
            {
                List.Add(set);
            }
            public VRActiveActionSet_t[] Get()
            {
                return List.ToArray();
            }
        }

        //デジタルボタンクラス
        public class DigitalAction
        {
            public bool Available = false; //利用可能か
            public OriginSource Origin = null; //取得元情報
            public bool State = false; //ボタン状態
            public bool Changed = false; //変化
            public float UpdateTime = float.NaN; //変化時間

            public DigitalAction(InputDigitalActionData_t Data, OriginSource OriginInfo)
            {
                Available = Data.bActive;
                Origin = OriginInfo;
                State = Data.bState;
                Changed = Data.bChanged;
                UpdateTime = Data.fUpdateTime;
            }
        }

        //アナログクラス
        public class AnalogAction
        {
            public bool Available = false; //利用可能か
            public OriginSource Origin = null; //取得元情報

            public float x = float.NaN; //アナログ状態
            public float y = float.NaN;
            public float z = float.NaN;

            public float deltaX = float.NaN; //アナログ変化
            public float deltaY = float.NaN;
            public float deltaZ = float.NaN;

            public float UpdateTime = float.NaN; //変化時間

            public AnalogAction(InputAnalogActionData_t Data, OriginSource OriginInfo)
            {
                Available = Data.bActive;
                Origin = OriginInfo;

                x = Data.x;
                y = Data.y;
                z = Data.z;

                deltaX = Data.deltaX;
                deltaY = Data.deltaY;
                deltaZ = Data.deltaZ;

                UpdateTime = Data.fUpdateTime;
            }
        }

        //姿勢クラス
        public class PoseAction
        {
            public bool Available = false; //利用可能か
            public OriginSource Origin = null; //取得元情報
            public ActionTransform transform = null; //姿勢

            public PoseAction(InputPoseActionData_t Data, OriginSource OriginInfo)
            {
                Available = Data.bActive;
                Origin = OriginInfo;

                TrackedDevicePose_t Pose = Data.pose;
                SteamVR_Utils.RigidTransform trans = new SteamVR_Utils.RigidTransform(Pose.mDeviceToAbsoluteTracking);
                transform = new ActionTransform();

                if (OriginInfo != null)
                {
                    transform.deviceid = OriginInfo.DeviceIndex;
                }
                else
                {
                    transform.deviceid = InvalidDeviceIndex;
                }

                //右手系・左手系の変換をした
                transform.velocity[0] = Pose.vVelocity.v0;
                transform.velocity[1] = Pose.vVelocity.v1;
                transform.velocity[2] = -Pose.vVelocity.v2;
                transform.angularVelocity[0] = -Pose.vAngularVelocity.v0;
                transform.angularVelocity[1] = -Pose.vAngularVelocity.v1;
                transform.angularVelocity[2] = Pose.vAngularVelocity.v2;

                transform.position = trans.pos;
                transform.rotation = trans.rot;
            }
        }

        //入力元デバイスクラス(GetOriginTrackedDeviceInfo関係)
        public class OriginSource
        {
            //デバイス情報
            public string DevicePath = null;
            public ulong DeviceInternalHandle = InvalidDeviceIndex;
            public uint DeviceIndex = InvalidDeviceIndex;
            public string RenderModelComponentName = null;

            public OriginSource(InputOriginInfo_t inputOrigin, ulong InternalHandle)
            {
                DeviceIndex = inputOrigin.trackedDeviceIndex;
                RenderModelComponentName = inputOrigin.rchRenderModelComponentName;
                DeviceInternalHandle = InternalHandle;
            }
        }

        //------------システム------------------

        public EasyOpenVRActionInput()
        {
            //とりあえず初期化する
            Init();
        }

        //初期化。失敗したらfalse
        public bool Init()
        {
            openvr = OpenVR.System;
            vrinput = OpenVR.Input;
            return IsReady();
        }

        //本ライブラリが利用可能か確認する
        public bool IsReady()
        {
            return openvr != null && vrinput != null;
        }

        //実行可能な状態かチェック
        public void ReadyCheck()
        {
            if (!IsReady())
            {
                //実行可能な状態にしようとしてみる
                if (!Init())
                {
                    //OpenVRが動作していないなどでやっぱり駄目だった
                    throw new InvalidOperationException();
                }
            }
        }


        //-------------OpenVRシステム----------------------

        //OpenVRを初期化する
        public bool StartOpenVR(EVRApplicationType type = EVRApplicationType.VRApplication_Overlay)
        {
            //すでに利用可能な場合は初期化しない(衝突する)
            if (Init())
            {
                return true;
            }

            //初期化する
            var openVRError = EVRInitError.None;
            openvr = OpenVR.Init(ref openVRError, type);
            if (openVRError != EVRInitError.None)
            {
                return false;
            }

            //本ライブラリも初期化
            return Init();
        }

        //VRシステムが使えるか確認する
        public bool CanUseOpenVR()
        {
            return OpenVR.System != null;
        }

        //-----------------イベント処理-------------------
        //終了イベントをキャッチした時に戻す
        public bool ProcessEventAndCheckQuit()
        {
            ReadyCheck(); //実行可能な状態かチェック

            //イベント構造体のサイズを取得
            uint uncbVREvent = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VREvent_t));

            //イベント情報格納構造体
            VREvent_t Event = new VREvent_t();
            //イベントを取り出す
            while (openvr.PollNextEvent(ref Event, uncbVREvent))
            {
                //イベント情報で分岐
                switch ((EVREventType)Event.eventType)
                {
                    case EVREventType.VREvent_Quit:
                        return true;
                }
            }
            return false;
        }

        //自動終了補助
        public void AutoExitOnQuit()
        {
            if (ProcessEventAndCheckQuit())
            {
                ApplicationQuit();
            }
        }


        //---------------ユーティリティ-----------------
        public void Set90fps()
        {
            Application.targetFrameRate = 90;
        }

        //アプリケーションを終了させる
        public void ApplicationQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
        }

        //---------------Setter----------------
        public void SetAutoUpdate(bool autoupdate)
        {
            this.autoupdate = autoupdate;
        }


        //---------------Getter------------------
        public TrackedDevicePose_t[] GetAllDevicePose()
        {
            if (autoupdate)
            {
                Update();
            }
            return allDevicePose;
        }

        public TrackedDevicePose_t GetDevicePose(uint i)
        {
            if (!IsDeviceValid(i))
            {
                return new TrackedDevicePose_t();
            }
            return allDevicePose[i];
        }

        public ETrackingResult GetDeviceTrackingResult(uint i)
        {
            if (!IsDeviceValid(i))
            {
                return ETrackingResult.Uninitialized;
            }
            return allDevicePose[i].eTrackingResult;
        }



        //---------------Device Index------------------

        public uint GetHMDIndex()
        {
            return OpenVR.k_unTrackedDeviceIndex_Hmd;
        }

        public uint GetLeftControllerIndex()
        {
            ReadyCheck();
            return openvr.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
        }

        public uint GetRightControllerIndex()
        {
            ReadyCheck(); //実行可能な状態かチェック
            return openvr.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
        }

        //---------------Device Status------------------

        //デバイスが有効か
        public bool IsDeviceValid(uint index)
        {
            ReadyCheck(); //実行可能な状態かチェック

            //自動更新処理
            if (autoupdate)
            {
                //前回と違うフレームの場合のみ更新
                if (LastFrameCount != Time.frameCount)
                {
                    UpdatePredictedTime(); //光子遅延時間のアップデート追加
                    Update();
                }
            }
            //情報が有効でないなら更新
            if (allDevicePose == null)
            {
                Update();
            }
            //それでも情報が有効でないなら失敗
            if (allDevicePose == null)
            {
                return false;
            }

            //device indexが有効
            if (index != OpenVR.k_unTrackedDeviceIndexInvalid)
            {
                //接続されていて姿勢情報が有効
                if (allDevicePose[index].bDeviceIsConnected && allDevicePose[index].bPoseIsValid)
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsDeviceConnected(uint idx)
        {
            ReadyCheck(); //実行可能な状態かチェック

            return openvr.IsTrackedDeviceConnected(idx);
        }

        //---------------Device Pose------------------

        //全デバイス情報を更新
        public void Update(ETrackingUniverseOrigin origin = ETrackingUniverseOrigin.TrackingUniverseStanding)
        {
            ReadyCheck(); //実行可能な状態かチェック

            allDevicePose = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            //すべてのデバイスの情報を取得
            openvr.GetDeviceToAbsoluteTrackingPose(origin, PredictedTime, allDevicePose);
            //最終更新フレームを更新
            LastFrameCount = Time.frameCount;
        }

        //指定デバイスの姿勢情報を取得
        public ActionTransform GetTransform(uint index)
        {
            //有効なデバイスか
            if (!IsDeviceValid(index))
            {
                return null;
            }

            TrackedDevicePose_t Pose = allDevicePose[index];
            SteamVR_Utils.RigidTransform trans = new SteamVR_Utils.RigidTransform(Pose.mDeviceToAbsoluteTracking);
            ActionTransform res = new ActionTransform();

            res.deviceid = index;

            //右手系・左手系の変換をした
            res.velocity[0] = Pose.vVelocity.v0;
            res.velocity[1] = Pose.vVelocity.v1;
            res.velocity[2] = -Pose.vVelocity.v2;
            res.angularVelocity[0] = -Pose.vAngularVelocity.v0;
            res.angularVelocity[1] = -Pose.vAngularVelocity.v1;
            res.angularVelocity[2] = Pose.vAngularVelocity.v2;

            res.position = trans.pos;
            res.rotation = trans.rot;

            return res;
        }

        public ActionTransform GetHMDTransform()
        {
            return GetTransform(GetHMDIndex());
        }

        public ActionTransform GetLeftControllerTransform()
        {
            return GetTransform(GetLeftControllerIndex());
        }

        public ActionTransform GetRightControllerTransform()
        {
            return GetTransform(GetRightControllerIndex());
        }

        //---------------GameObject Transform------------------
        public void SetGameObjectTransform(ref UnityEngine.GameObject obj, ActionTransform transform)
        {
            if (transform == null)
            {
                return;
            }
            obj.transform.position = transform.position;
            obj.transform.rotation = transform.rotation;
        }

        public void SetGameObjectLocalTransform(ref UnityEngine.GameObject obj, ActionTransform transform)
        {
            if (transform == null)
            {
                return;
            }
            obj.transform.localPosition = transform.position;
            obj.transform.localRotation = transform.rotation;
        }

        public void SetGameObjectTransformWithOffset(ref UnityEngine.GameObject obj, ActionTransform transform, ActionTransform transformOffset)
        {
            if (transform == null)
            {
                return;
            }
            if (transformOffset == null)
            {
                transformOffset = new ActionTransform();
            }

            Debug.Log(transform.position.ToString());
            Debug.Log(transformOffset.position.ToString());
            Debug.Log((transform.position - transformOffset.position).ToString());

            obj.transform.position = transform.position - transformOffset.position;
            obj.transform.rotation = transform.rotation * Quaternion.Inverse(transformOffset.rotation);
        }

        public void SetGameObjectLocalTransformWithOffset(ref UnityEngine.GameObject obj, ActionTransform transform, ActionTransform transformOffset)
        {
            if (transform == null)
            {
                return;
            }
            if (transformOffset == null)
            {
                transformOffset = new ActionTransform();
            }

            obj.transform.localPosition = transform.position - transformOffset.position;
            obj.transform.localRotation = transform.rotation * Quaternion.Inverse(transformOffset.rotation);
        }

        //---------------Device Property------------------
        
　      //低レベル関数
        public bool GetPropertyString(uint idx, ETrackedDeviceProperty prop, out string result)
        {
            ReadyCheck(); //実行可能な状態かチェック

            result = null;
            ETrackedPropertyError error = new ETrackedPropertyError();
            //device情報を取得するのに必要な文字数を取得
            uint size = openvr.GetStringTrackedDeviceProperty(idx, prop, null, 0, ref error);
            if (error != ETrackedPropertyError.TrackedProp_BufferTooSmall)
            {
                return false;
            }

            StringBuilder s = new StringBuilder();
            s.Length = (int)size; //文字長さ確保
                                  //device情報を取得する
            openvr.GetStringTrackedDeviceProperty(idx, prop, s, size, ref error);

            result = s.ToString();
            return (error == ETrackedPropertyError.TrackedProp_Success);
        }

        public bool GetPropertyFloat(uint idx, ETrackedDeviceProperty prop, out float result)
        {
            ReadyCheck(); //実行可能な状態かチェック

            ETrackedPropertyError error = new ETrackedPropertyError();
            result = openvr.GetFloatTrackedDeviceProperty(idx, prop, ref error);
            return (error == ETrackedPropertyError.TrackedProp_Success);
        }

        public bool GetPropertyBool(uint idx, ETrackedDeviceProperty prop, out bool result)
        {
            ReadyCheck(); //実行可能な状態かチェック

            ETrackedPropertyError error = new ETrackedPropertyError();
            result = openvr.GetBoolTrackedDeviceProperty(idx, prop, ref error);
            return (error == ETrackedPropertyError.TrackedProp_Success);
        }

        public bool GetPropertyUint64(uint idx, ETrackedDeviceProperty prop, out ulong result)
        {
            ReadyCheck(); //実行可能な状態かチェック

            ETrackedPropertyError error = new ETrackedPropertyError();
            result = openvr.GetUint64TrackedDeviceProperty(idx, prop, ref error);
            return (error == ETrackedPropertyError.TrackedProp_Success);
        }

        public bool GetPropertyInt32(uint idx, ETrackedDeviceProperty prop, out int result)
        {
            ReadyCheck(); //実行可能な状態かチェック

            ETrackedPropertyError error = new ETrackedPropertyError();
            result = openvr.GetInt32TrackedDeviceProperty(idx, prop, ref error);
            return (error == ETrackedPropertyError.TrackedProp_Success);
        }

        //中レベル関数
        public string GetPropertyStringWhenConnected(uint idx, ETrackedDeviceProperty prop)
        {
            if (!IsDeviceConnected(idx))
            {
                return null;
            }

            string result = null;
            if (!GetPropertyString(idx, prop, out result))
            {
                return null;
            }
            return result;
        }

        public float GetPropertyFloatWhenConnected(uint idx, ETrackedDeviceProperty prop)
        {
            if (!IsDeviceConnected(idx))
            {
                return float.NaN;
            }

            float result = float.NaN;
            if (!GetPropertyFloat(idx, prop, out result))
            {
                return float.NaN;
            }
            return result;
        }

        //高レベル関数
        //シリアル番号を取得する
        public string GetSerialNumber(uint idx)
        {
            return GetPropertyStringWhenConnected(idx, ETrackedDeviceProperty.Prop_SerialNumber_String);
        }

        //型式名を取得する
        public string GetRenderModelName(uint idx)
        {
            return GetPropertyStringWhenConnected(idx, ETrackedDeviceProperty.Prop_RenderModelName_String);
        }

        //型式名を取得する
        public string GetRegisteredDeviceType(uint idx)
        {
            return GetPropertyStringWhenConnected(idx, ETrackedDeviceProperty.Prop_RegisteredDeviceType_String);
        }

        //電池残量を取得する
        public float GetDeviceBatteryPercentage(uint idx)
        {
            return GetPropertyFloatWhenConnected(idx, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float) * 100.0f;
        }

        //充電中か調べる
        public bool IsCharging(uint idx, out bool result)
        {
            return GetPropertyBool(idx, ETrackedDeviceProperty.Prop_DeviceIsCharging_Bool, out result);
        }

        //スクリーンショットを取る(要る?)
        public bool TakeScreenShot(string path, string pathVR)
        {
            ReadyCheck(); //実行可能な状態かチェック

            CVRScreenshots screenshot = OpenVR.Screenshots;
            if (screenshot == null)
            {
                return false;
            }
            string previewfile = path;
            string vrfile = pathVR;

            EVRScreenshotError error = EVRScreenshotError.None;
            uint pOutScreenshotHandle = 0;
            error = screenshot.TakeStereoScreenshot(ref pOutScreenshotHandle, previewfile, vrfile);
            return (error == EVRScreenshotError.None);
        }

        //----------------光子遅延時間----------------------------

        //予測遅延時間(動作-光子遅延時間)を設定
        public void UpdatePredictedTime()
        {
            PredictedTime = GetPredictedTime();
        }

        //予測遅延時間(動作-光子遅延時間)を無効化
        public void ClearPredictedTime()
        {
            PredictedTime = 0;
        }

        //現在の予測遅延時間(動作-光子遅延時間)を取得
        public float GetPredictedTime()
        {
            //最後のVsyncからの経過時間(フレーム経過時間)を取得
            float FrameTime = 0;
            ulong FrameCount = 0;

            ReadyCheck(); //実行可能な状態かチェック

            if (!openvr.GetTimeSinceLastVsync(ref FrameTime, ref FrameCount))
            {
                return 0; //有効な値を取得できなかった
            }

            //たまにすごい勢いで増えることがある
            if (FrameTime > 1.0f)
            {
                return 0; //有効な値を取得できなかった
            }

            //1フレームあたりの時間取得
            float DisplayFrequency = 0;
            if (!GetPropertyFloat(GetHMDIndex(), ETrackedDeviceProperty.Prop_DisplayFrequency_Float, out DisplayFrequency))
            {
                return 0; //有効な値を取得できなかった
            }
            float DisplayCycle = 1f / DisplayFrequency;

            //光子遅延時間(出力からHMD投影までにかかる時間)取得
            float PhotonDelay = 0;
            if (!GetPropertyFloat(GetHMDIndex(), ETrackedDeviceProperty.Prop_SecondsFromVsyncToPhotons_Float, out PhotonDelay))
            {
                return 0; //有効な値を取得できなかった
            }

            //予測遅延時間(1フレームあたりの時間 - 現在フレーム経過時間 + 光子遅延時間)
            var PredictedTimeNow = DisplayCycle - FrameTime + PhotonDelay;

            //負の値は過去になる。
            if (PredictedTimeNow < 0)
            {
                return 0;
            }

            return PredictedTimeNow;
        }

        //----------------アクションシステム----------------------------

        //アクションシステムを初期化
        public bool InitActionSystem(string ActionManifestPath = "")
        {
            EVRInputError inputError = EVRInputError.None;

            //VRシステムが使えるか
            ReadyCheck(); //実行可能な状態かチェック

            //空白ならカレントディレクトリ
            if (ActionManifestPath == "") {
                ActionManifestPath = Directory.GetCurrentDirectory() + "\\actions.json";
            }

            //カレントパスのActionManifestを設定(これによりLegacy Modeにならなくなる)
            inputError = vrinput.SetActionManifestPath(ActionManifestPath);

            //エラーが起きたとき(ただしミスマッチエラーは起きうるので無視)
            if (inputError != EVRInputError.None && inputError != EVRInputError.MismatchedActionManifest)
            {
                //Steam VRと通信ができていないので強制終了する
                if (inputError == EVRInputError.IPCError)
                {
                    Debug.LogError("Emergency Stop(DLL Handle Invalid!)");
                    ApplicationQuit();
                }

                //基本ここでエラーが起きた場合は致命的である
                throw new IOException(inputError.ToString());
            }
            return true;
        }

        //---------------アクション登録-------------------

        //アクションセットを登録してハンドルを格納
        public void RegisterActionSet(string path)
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;
            ulong handle = InvalidInputHandle;

            //ハンドルが存在しない場合登録。すでにある場合は無視
            if (!ActionSetHandles.ContainsKey(path))
            {
                inputError = vrinput.GetActionSetHandle(path, ref handle);
                if (inputError != EVRInputError.None)
                {
                    //だいたいハンドル名が間違っている。いずれにせよ致命的エラー
                    throw new IOException(inputError.ToString());
                }
                ActionSetHandles.Add(path, handle);
            }
            return;
        }

        //アクションを登録してハンドルを格納
        public void RegisterAction(string path)
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;
            ulong handle = InvalidInputHandle;

            //ハンドルが存在しない場合登録。すでにある場合は無視
            if (!ActionHandles.ContainsKey(path))
            {
                inputError = vrinput.GetActionHandle(path, ref handle);
                if (inputError != EVRInputError.None)
                {
                    //だいたいハンドル名が間違っている。いずれにせよ致命的エラー
                    throw new IOException(inputError.ToString());
                }
                ActionHandles.Add(path, handle);
            }
            return;
        }

        //InputSourceを登録してハンドルを格納
        public void RegisterInputSource(string path)
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;
            ulong handle = InvalidInputHandle;

            //ハンドルが存在しない場合登録。すでにある場合は無視
            if (!InputSourceHandles.ContainsKey(path))
            {
                inputError = vrinput.GetInputSourceHandle(path, ref handle);
                if (inputError != EVRInputError.None)
                {
                    //だいたいハンドル名が間違っている。いずれにせよ致命的エラー
                    throw new IOException(inputError.ToString());
                }
                InputSourceHandles.Add(path, handle);
            }
            return;
        }

        //---------------アクション取り出し-------------------

        //アクションセットからハンドルを探します。
        public ulong GetActionSetHandle(string path)
        {
            ReadyCheck(); //実行可能な状態かチェック

            ulong handle = InvalidInputHandle;
            if (!ActionSetHandles.TryGetValue(path, out handle))
            {
                //だいたいハンドル名が間違っている。いずれにせよ致命的エラー
                throw new KeyNotFoundException("ActionSetHandle not found.");
            }
            return handle;
        }

        //アクションからハンドルを探します。
        public ulong GetActionHandle(string path)
        {
            ReadyCheck(); //実行可能な状態かチェック

            ulong handle = InvalidInputHandle;
            if (!ActionHandles.TryGetValue(path, out handle))
            {
                //だいたいハンドル名が間違っている。いずれにせよ致命的エラー
                throw new KeyNotFoundException("ActionHandle not found.");
            }
            return handle;
        }

        //InputSourceからハンドルを探します。
        public ulong GetInputSourceHandle(string path)
        {
            ReadyCheck(); //実行可能な状態かチェック

            ulong handle = InvalidInputHandle;
            if (!InputSourceHandles.TryGetValue(path, out handle))
            {
                //だいたいハンドル名が間違っている。いずれにせよ致命的エラー
                throw new KeyNotFoundException("InputSource not found.");
            }
            return handle;
        }

        //---------------アクション辞書取り出し-------------------

        //生辞書が欲しい方向け(ActionSets)
        public Dictionary<string, ulong> GetActionSetHandles()
        {
            return ActionSetHandles;
        }

        //生辞書が欲しい方向け(Action)
        public Dictionary<string, ulong> GetActionHandles()
        {
            return ActionHandles;
        }

        //生辞書が欲しい方向け(InputSource)
        public Dictionary<string, ulong> GetInputSourceHandles()
        {
            return InputSourceHandles;
        }

        //---------------アクション更新-------------------

        //VRActiveActionSetを生成する。これを配列に突っ込んでUpdateActionSetStateを呼び出す
        public void AddActiveActionSet(ActiveActionSets ActionSets, string ActionSetPath, string RestrictInputSourcePath = "", string SecondaryActionSetPath = "")
        {
            VRActiveActionSet_t sets = new VRActiveActionSet_t();
            sets.ulActionSet = GetActionSetHandle(ActionSetPath); //更新対象
            sets.ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle; //制限なし
            sets.ulSecondaryActionSet = OpenVR.k_ulInvalidActionSetHandle; //無効値を設定

            //制約デバイスが指定されているならば適用
            if (RestrictInputSourcePath != "")
            {
                sets.ulRestrictedToDevice = GetInputSourceHandle(RestrictInputSourcePath); //制約デバイス
            }
            //セカンダリアクションセットが指定されているならば適用
            if (SecondaryActionSetPath != "")
            {
                sets.ulSecondaryActionSet = GetActionSetHandle(SecondaryActionSetPath); //無効値を設定
            }

            ActionSets.Add(sets);
        }

        //アクションセットを更新する
        public void UpdateActionSetState(ActiveActionSets ActiveSets)
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;

            //サイズ取得
            var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRActiveActionSet_t));
            //更新処理実行
            inputError = vrinput.UpdateActionState(ActiveSets.Get(), size);

            //ここでエラーになることはそうそうない
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }
            return;
        }

        //---------------アクション取得系-------------------

        //デジタルボタンの状態を取得する(独自クラス)
        public DigitalAction GetDigitalActionData(string ActionPath, string RestrictToDevicePath = "")
        {
            InputDigitalActionData_t data;
            GetDigitalActionDataRaw(ActionPath, out data, RestrictToDevicePath);

            return new DigitalAction(data, GetOriginSourceFromInternalHandle(data.activeOrigin));
        }

        //デジタルボタンの状態を取得する(生データ)
        private void GetDigitalActionDataRaw(string ActionPath, out InputDigitalActionData_t data, string RestrictToDevicePath = "")
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;

            data = new InputDigitalActionData_t();

            var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(InputDigitalActionData_t));
            ulong handle = GetActionHandle(ActionPath); //無効なハンドルならthrowされる

            //制約デバイス指定されていれば適用
            ulong DeviceHandle = OpenVR.k_ulInvalidInputValueHandle;
            if (RestrictToDevicePath != "")
            {
                DeviceHandle = GetInputSourceHandle(RestrictToDevicePath); //無効なハンドルならthrowされる
            }

            //取得処理
            inputError = vrinput.GetDigitalActionData(handle, ref data, size, DeviceHandle);
            if (inputError == EVRInputError.WrongType)
            {
                //デジタルボタンではない
                throw new ArgumentException(inputError.ToString());
            }
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }

            return;
        }

        //アナログ軸の状態を取得する(独自クラス)
        public AnalogAction GetAnalogActionData(string ActionPath, string RestrictToDevicePath = "")
        {
            InputAnalogActionData_t data;
            GetAnalogActionDataRaw(ActionPath, out data, RestrictToDevicePath);

            return new AnalogAction(data, GetOriginSourceFromInternalHandle(data.activeOrigin));
        }

        //アナログ軸の状態を取得する(生データ)
        private void GetAnalogActionDataRaw(string ActionPath, out InputAnalogActionData_t data, string RestrictToDevicePath = "")
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;

            data = new InputAnalogActionData_t();

            var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(InputAnalogActionData_t));
            ulong handle = GetActionHandle(ActionPath); //無効なハンドルならthrowされる

            //制約デバイス指定されていれば適用
            ulong DeviceHandle = OpenVR.k_ulInvalidInputValueHandle;
            if (RestrictToDevicePath != "")
            {
                DeviceHandle = GetInputSourceHandle(RestrictToDevicePath); //無効なハンドルならthrowされる
            }

            //取得処理
            inputError = vrinput.GetAnalogActionData(handle, ref data, size, DeviceHandle);
            if (inputError == EVRInputError.WrongType)
            {
                //アナログ軸ではない
                throw new ArgumentException(inputError.ToString());
            }
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }

            return;
        }

        //姿勢を取得する(独自クラス)
        public PoseAction GetPoseActionData(string ActionPath, ETrackingUniverseOrigin UniverseOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding , string RestrictToDevicePath = "")
        {
            InputPoseActionData_t data;
            GetPoseActionDataRaw(ActionPath, out data, UniverseOrigin, RestrictToDevicePath);

            return new PoseAction(data, GetOriginSourceFromInternalHandle(data.activeOrigin));
        }

        //姿勢を取得する(生データ)
        private void GetPoseActionDataRaw(string ActionPath, out InputPoseActionData_t data, ETrackingUniverseOrigin UniverseOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding, string RestrictToDevicePath = "")
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;

            data = new InputPoseActionData_t();

            var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(InputPoseActionData_t));
            ulong handle = GetActionHandle(ActionPath); //無効なハンドルならthrowされる

            //制約デバイス指定されていれば適用
            ulong DeviceHandle = OpenVR.k_ulInvalidInputValueHandle;
            if (RestrictToDevicePath != "")
            {
                DeviceHandle = GetInputSourceHandle(RestrictToDevicePath); //無効なハンドルならthrowされる
            }

            //取得処理
            inputError = vrinput.GetPoseActionData(handle, UniverseOrigin, PredictedTime, ref data, size, DeviceHandle);
            if (inputError == EVRInputError.WrongType)
            {
                //姿勢ではない
                throw new ArgumentException(inputError.ToString());
            }
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }

            return;
        }

        //---------------アクション設定系-------------------

        //振動を発生させる
        public void TriggerHaptic(string ActionPath, float StartTime, float DurationTime, float Frequency, float Amplitude, string RestrictToDevicePath = "")
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;
            ulong handle = GetActionHandle(ActionPath); //無効なハンドルならthrowされる

            //制約デバイス指定されていれば適用
            ulong DeviceHandle = OpenVR.k_ulInvalidInputValueHandle;
            if (RestrictToDevicePath != "")
            {
                DeviceHandle = GetInputSourceHandle(RestrictToDevicePath); //無効なハンドルならthrowされる
            }

            //取得処理
            inputError = vrinput.TriggerHapticVibrationAction(handle, StartTime, DurationTime, Frequency, Amplitude, DeviceHandle);
            if (inputError == EVRInputError.WrongType)
            {
                //姿勢ではない
                throw new ArgumentException(inputError.ToString());
            }
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }

            return;
        }


        //---------------アクション情報取得系-------------------

        //ハンドルから入力元デバイスの情報を取得する
        //また入力ソースハンドルを逆引きしてpathにまで戻す。
        public OriginSource GetOriginSourceFromInternalHandle(ulong Handle)
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;

            //デバイス情報を取得
            InputOriginInfo_t originInfo = new InputOriginInfo_t();
            var size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(InputOriginInfo_t));
            inputError = vrinput.GetOriginTrackedDeviceInfo(Handle, ref originInfo, size);
            if (inputError != EVRInputError.None)
            {
                return null; //情報なし
            }

            //デバイス情報を作成
            OriginSource origin = new OriginSource(originInfo, Handle);

            //デバイスハンドルから探索して見つかれば格納
            foreach (KeyValuePair<string, ulong> p in InputSourceHandles)
            {
                if (p.Value == originInfo.devicePath)
                {
                    origin.DevicePath = p.Key;
                }
            }

            return origin;
        }

        //ActionセットとActionを指定すると、それが取得できる元のパスのListを返す
        public List<OriginSource> GetActionOriginsList(string ActionSetPath, string ActionPath) {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;
            ulong sethandle = GetActionSetHandle(ActionSetPath); //無効なハンドルならthrowされる
            ulong handle = GetActionHandle(ActionPath); //無効なハンドルならthrowされる

            //取得処理
            ulong[] origins = new ulong[1024];
            inputError = vrinput.GetActionOrigins(sethandle,handle,origins);
            if (inputError == EVRInputError.WrongType)
            {
                //姿勢ではない
                throw new ArgumentException(inputError.ToString());
            }
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }

            List<OriginSource> list = new List<OriginSource>();
            for (int i = 0; i < 1024; i++) {
                if (origins[i] == OpenVR.k_ulInvalidInputValueHandle)
                {
                    break;
                }
                OriginSource origin = GetOriginSourceFromInternalHandle(origins[i]);
                if (origin != null) {
                    list.Add(origin);
                }
            }

            return list;
        }

        //originを渡すと、そのコントローラのローカライズされた名前が出る(ゲーム内説明用)
        public string GetLocalizedButtonNameFromOriginSource(OriginSource origin) {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;

            //取得処理
            StringBuilder s = new StringBuilder();
            s.Length = 8192;

            inputError = vrinput.GetOriginLocalizedName(origin.DeviceInternalHandle, s, 8192);
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }

            return s.ToString();

        }

        //アクションを渡すとアクションに関する情報を表示する(Binding UIを開く)
        public void ShowActionBinding(string ActionSetPath, string ActionPath)
        {
            ReadyCheck(); //実行可能な状態かチェック

            EVRInputError inputError = EVRInputError.None;
            ulong sethandle = GetActionSetHandle(ActionSetPath); //無効なハンドルならthrowされる
            ulong handle = GetActionHandle(ActionPath); //無効なハンドルならthrowされる

            inputError = vrinput.ShowActionOrigins(sethandle, handle);
            if (inputError != EVRInputError.None)
            {
                //致命的エラー
                throw new IOException(inputError.ToString());
            }
            return;
        }
        
    }
}