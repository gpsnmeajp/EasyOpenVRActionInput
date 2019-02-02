using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using EasyLazyLibrary;

public class InputTestScript : MonoBehaviour {
    EasyOpenVRActionInput input = new EasyOpenVRActionInput();

    public GameObject obj1;
    public GameObject obj2;
    public GameObject obj3;

    //取得したいアクションセットを指定
    EasyOpenVRActionInput.ActiveActionSets sets = new EasyOpenVRActionInput.ActiveActionSets();

    void Start () {
        //初期化
        input.StartOpenVR();

        //Steam VR Input System初期化
        input.InitActionSystem();

        //取得したいデバイス、アクションセット、アクションを登録してハンドルを内部登録
        input.RegisterInputSource("/user/head");
        input.RegisterInputSource("/user/hand/left");
        input.RegisterInputSource("/user/hand/right");

        input.RegisterActionSet("/actions/default");
        input.RegisterAction("/actions/default/in/InteractUI");
        input.RegisterAction("/actions/default/in/Pose");
        input.RegisterAction("/actions/default/out/Haptic");

        input.RegisterActionSet("/actions/buggy");
        input.RegisterAction("/actions/buggy/in/Steering");

        //更新したいセットを指定
        input.AddActiveActionSet(sets, "/actions/default");
        input.AddActiveActionSet(sets, "/actions/buggy");

        //ある入力に関連付けられたボタン一覧表示
        var l = input.GetActionOriginsList("/actions/default", "/actions/default/in/InteractUI");
        foreach (var x in l) {
            Debug.Log(x.DevicePath + " / " + input.GetLocalizedButtonNameFromOriginSource(x));
        }
        Debug.Log("---------------");

    }
    bool f = false;
    void Update()
    {
        //セットを更新
        input.UpdateActionSetState(sets);

        //光子遅延時間を更新
        input.UpdatePredictedTime();

        //デジタルボタンを取得
        EasyOpenVRActionInput.DigitalAction d = input.GetDigitalActionData("/actions/default/in/InteractUI");
        Debug.Log(d.State);
        if (d.State && !f) {
            input.ShowActionBinding("/actions/default","/actions/default/out/Haptic");
            //input.TriggerHaptic("/actions/default/out/Haptic", 10, 1f, 100, 1, "/user/hand/left");
            f = true;
        }

        //アナログ軸を取得
        EasyOpenVRActionInput.AnalogAction a = input.GetAnalogActionData("/actions/buggy/in/Steering");
        Debug.Log(a.x +" / "+a.y+" / "+a.z);
        if (a.Origin != null) {
            Debug.Log(a.Origin.DevicePath);
        }

        //姿勢取得
        EasyOpenVRActionInput.PoseAction p = input.GetPoseActionData("/actions/default/in/Pose");
        if (p.Origin != null)
        {
            Debug.Log(p.Origin.DevicePath);
            Debug.Log(p.transform.ToString());
            input.SetGameObjectTransform(ref obj3, p.transform);
        }

        //姿勢反映テスト
        input.SetGameObjectTransform(ref obj1, input.GetHMDTransform());
        input.SetGameObjectTransform(ref obj2, input.GetRightControllerTransform());
    }


    public void getAction()
    {
        //util.getAction();
    }
}
