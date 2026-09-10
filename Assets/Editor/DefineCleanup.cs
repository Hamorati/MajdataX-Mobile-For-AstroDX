using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>移除 MAJDATA_MOBILE_DEBUG 定义（恢复标准桌面构建）。</summary>
public static class DefineCleanup
{
    public static void RemoveMobileDebug()
    {
        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        if (defines.Contains("MAJDATA_MOBILE_DEBUG"))
        {
            defines = defines.Replace("MAJDATA_MOBILE_DEBUG", "").Replace(";;", ";").Trim(';');
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, defines);
            Debug.Log("MAJDATA_MOBILE_DEBUG removed. Standalone defines now: '" + defines + "'");
        }
        else
        {
            Debug.Log("MAJDATA_MOBILE_DEBUG not present.");
        }
    }
}
