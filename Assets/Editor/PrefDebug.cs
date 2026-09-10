using System;
using UnityEditor;
using UnityEngine;

public static class PrefDebug
{
    public static void Dump()
    {
        // 列出与 Android 相关的 EditorPrefs
        var allKeys = new System.Collections.Generic.List<string>();
        // EditorPrefs 没有枚举 API：直接读注册表位置
        var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Unity Technologies\Unity Editor 6000.3.19f1");
        if (key != null)
        {
            foreach (var name in key.GetValueNames())
                if (name.Contains("Android") || name.Contains("Jdk"))
                    Debug.Log($"PREF {name} = {key.GetValue(name)}");
            key.Close();
        }
        else
        {
            Debug.Log("registry key not found");
        }
        Debug.Log($"AndroidSdkRoot={EditorPrefs.GetString("AndroidSdkRoot", "<null>")}");
        Debug.Log($"AndroidNdkRoot={EditorPrefs.GetString("AndroidNdkRoot", "<null>")}");
        Debug.Log($"AndroidNdkRootR23B={EditorPrefs.GetString("AndroidNdkRootR23B", "<null>")}");
        Debug.Log($"JdkPath={EditorPrefs.GetString("JdkPath", "<null>")}");
    }
}
