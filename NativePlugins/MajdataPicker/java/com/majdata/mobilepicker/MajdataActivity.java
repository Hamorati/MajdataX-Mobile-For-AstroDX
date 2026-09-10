package com.majdata.mobilepicker;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.DocumentsContract;
import android.provider.OpenableColumns;
import android.webkit.MimeTypeMap;
import android.util.Log;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerActivity;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * 移动端音频选择插件：
 * 1) 作为 launcher Activity（继承 UnityPlayerActivity，manifest 中替换 Unity 默认启动项）；
 * 2) 通过系统 SAF（ACTION_OPEN_DOCUMENT, audio/*）选择音频文件；
 * 3) 把选中的内容流复制到目标目录下的 .picked<ext> 文件，供 Unity 侧改名为 track.<ext>；
 * 4) 通过 ACTION_OPEN_DOCUMENT_TREE 选择谱面文件夹并导入 Charts 目录（Android 11+ 分区存储下
 *    用户无法直接访问应用私有目录，导入是唯一可行的取谱途径）。
 */
public class MajdataActivity extends UnityPlayerActivity {

    private static final String TAG = "MajdataPicker";

    private static final int REQ_PICK_AUDIO = 1001;
    private static final int REQ_PICK_CHART_FOLDER = 1002;
    private static final int REQ_PICK_IMAGE = 1003;
    private static final int REQ_PICK_EXPORT_FOLDER = 1004;

    private static volatile String sPendingDir;
    private static volatile String sResultPath;
    private static volatile String sError;
    private static volatile boolean sDone;

    private static volatile String sChartResult;
    private static volatile String sChartError;
    private static volatile boolean sChartDone;

    private static volatile String sExportZipPath;
    private static volatile String sExportError;
    private static volatile boolean sExportDone;

    /** Unity 主线程调用：发起系统音频选择器。targetDir 必须已存在。 */
    public static void PickAudio(String targetDir) {
        sPendingDir = targetDir;
        sResultPath = null;
        sError = null;
        sDone = false;

        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sError = "no unity activity";
            sDone = true;
            return;
        }
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("audio/*");
        intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, false);
        try {
            activity.startActivityForResult(intent, REQ_PICK_AUDIO);
        } catch (Exception e) {
            sError = "startActivityForResult failed: " + e.getMessage();
            sDone = true;
        }
    }

    /** Unity 主线程调用：发起系统文件夹选择器（谱面文件夹导入）。 */
    public static void PickChartFolder(String destRoot) {
        sPendingDir = destRoot;
        sChartResult = null;
        sChartError = null;
        sChartDone = false;

        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sChartError = "no unity activity";
            sChartDone = true;
            return;
        }
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        try {
            activity.startActivityForResult(intent, REQ_PICK_CHART_FOLDER);
        } catch (Exception e) {
            sChartError = "startActivityForResult failed: " + e.getMessage();
            sChartDone = true;
        }
    }

    /** Unity 主线程调用：发起系统图片选择器（编辑器背景图）。targetDir 必须已存在。 */
    public static void PickImage(String targetDir) {
        sPendingDir = targetDir;
        sResultPath = null;
        sError = null;
        sDone = false;

        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sError = "no unity activity";
            sDone = true;
            return;
        }
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/*");
        intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, false);
        try {
            activity.startActivityForResult(intent, REQ_PICK_IMAGE);
        } catch (Exception e) {
            sError = "startActivityForResult failed: " + e.getMessage();
            sDone = true;
        }
    }

    /** Unity 侧轮询：返回 "path|error"；无结果时返回空串。 */
    public static String QueryResult() {
        if (!sDone) return "";
        sDone = false;
        String r = sResultPath;
        String e = sError;
        sResultPath = null;
        sError = null;
        return (r != null ? r : "") + "|" + (e != null ? e : "");
    }

    /** Unity 侧轮询谱面导入结果：返回 "导入数量|错误"；进行中返回空串。 */
    public static String QueryChartResult() {
        if (!sChartDone) return "";
        sChartDone = false;
        String r = sChartResult;
        String e = sChartError;
        sChartResult = null;
        sChartError = null;
        return (r != null ? r : "") + "|" + (e != null ? e : "");
    }

    /** Unity 主线程调用：发起系统文件夹选择器（导出 zip 目标目录）。zipPath 为应用内部已生成的 zip。 */
    public static void PickExportFolder(String zipPath) {
        sExportZipPath = zipPath;
        sExportError = null;
        sExportDone = false;

        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sExportError = "no unity activity";
            sExportDone = true;
            return;
        }
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        try {
            activity.startActivityForResult(intent, REQ_PICK_EXPORT_FOLDER);
        } catch (Exception e) {
            sExportError = "startActivityForResult failed: " + e.getMessage();
            sExportDone = true;
        }
    }

    /** Unity 侧轮询导出结果：进行中返回空串；成功返回 "OK"；失败返回 "ERR|错误信息"。 */
    public static String QueryExportResult() {
        if (!sExportDone) return "";
        sExportDone = false;
        String e = sExportError;
        sExportError = null;
        return e != null ? "ERR|" + e : "OK";
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == REQ_PICK_AUDIO) {
            HandleAudioResult(resultCode, data);
            return;
        }
        if (requestCode == REQ_PICK_IMAGE) {
            HandleImageResult(resultCode, data);
            return;
        }
        if (requestCode == REQ_PICK_CHART_FOLDER) {
            HandleChartFolderResult(resultCode, data);
            return;
        }
        if (requestCode == REQ_PICK_EXPORT_FOLDER) {
            HandleExportResult(resultCode, data);
            return;
        }
        super.onActivityResult(requestCode, resultCode, data);
    }

    private void HandleExportResult(int resultCode, Intent data) {
        Log.i(TAG, "HandleExportResult: requestCode=1004 resultCode=" + resultCode
                + " data=" + (data != null ? String.valueOf(data.getData()) : "null"));
        if (resultCode != RESULT_OK || data == null || data.getData() == null) {
            sExportError = "用户取消";
            sExportDone = true;
            return;
        }
        try {
            Uri treeUri = data.getData();
            try {
                getContentResolver().takePersistableUriPermission(treeUri,
                        Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            } catch (Exception ignored) { }

            File src = new File(sExportZipPath);
            if (!src.exists()) {
                sExportError = "zip 文件不存在";
                sExportDone = true;
                return;
            }
            String docId = CreateDocumentInTree(treeUri, src.getName(), "application/zip");
            if (docId == null) {
                sExportError = "无法在目标目录创建文件";
                sExportDone = true;
                return;
            }
            Uri docUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, docId);
            long copied = 0;
            try (InputStream in = new FileInputStream(src);
                 OutputStream out = getContentResolver().openOutputStream(docUri)) {
                if (out == null) throw new IllegalStateException("cannot open output stream");
                byte[] buf = new byte[65536];
                int n;
                while ((n = in.read(buf)) > 0) { out.write(buf, 0, n); copied += n; }
            }
            Log.i(TAG, "HandleExportResult: OK tree=" + treeUri + " docId=" + docId
                    + " copied=" + copied + " bytes");
            // 成功：错误保持空串
        } catch (Exception ex) {
            Log.w(TAG, "HandleExportResult failed", ex);
            sExportError = "导出失败: " + ex.getMessage();
        }
        sExportDone = true;
    }

    /** 在所选树目录中创建文档；同名或创建失败时自动尝试 _1/_2… 后缀。返回 documentId，失败返回 null。 */
    private String CreateDocumentInTree(Uri treeUri, String name, String mime) {
        String rootId = DocumentsContract.getTreeDocumentId(treeUri);
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, rootId);
        for (int i = 0; i < 20; i++) {
            String candidate = (i == 0) ? name : InsertSuffix(name, i);
            try {
                Uri doc = DocumentsContract.createDocument(getContentResolver(), children, mime, candidate);
                if (doc != null) return DocumentsContract.getDocumentId(doc);
            } catch (Exception ignored) {
                // 同名冲突或提供者拒绝 → 尝试下一个后缀
            }
        }
        return null;
    }

    private static String InsertSuffix(String name, int i) {
        int dot = name.lastIndexOf('.');
        if (dot <= 0) return name + "_" + i;
        return name.substring(0, dot) + "_" + i + name.substring(dot);
    }

    private void HandleImageResult(int resultCode, Intent data) {
        if (resultCode != RESULT_OK || data == null || data.getData() == null) {
            sError = "用户取消";
            sDone = true;
            return;
        }
        try {
            Uri uri = data.getData();
            String ext = GuessExtension(uri);
            File out = new File(sPendingDir, ".pickedbg" + ext);
            try (InputStream in = getContentResolver().openInputStream(uri);
                 FileOutputStream fos = new FileOutputStream(out)) {
                if (in == null) throw new IllegalStateException("cannot open input stream");
                byte[] buf = new byte[65536];
                int n;
                while ((n = in.read(buf)) > 0) fos.write(buf, 0, n);
            }
            sResultPath = out.getAbsolutePath();
        } catch (Exception ex) {
            sError = "copy failed: " + ex.getMessage();
        }
        sDone = true;
    }

    private void HandleAudioResult(int resultCode, Intent data) {
        if (resultCode != RESULT_OK || data == null || data.getData() == null) {
            sError = "用户取消";
            sDone = true;
            return;
        }
        try {
            Uri uri = data.getData();
            String ext = GuessExtension(uri);
            File out = new File(sPendingDir, ".picked" + ext);
            try (InputStream in = getContentResolver().openInputStream(uri);
                 FileOutputStream fos = new FileOutputStream(out)) {
                if (in == null) throw new IllegalStateException("cannot open input stream");
                byte[] buf = new byte[65536];
                int n;
                while ((n = in.read(buf)) > 0) fos.write(buf, 0, n);
            }
            sResultPath = out.getAbsolutePath();
        } catch (Exception ex) {
            sError = "copy failed: " + ex.getMessage();
        }
        sDone = true;
    }

    private void HandleChartFolderResult(int resultCode, Intent data) {
        if (resultCode != RESULT_OK || data == null || data.getData() == null) {
            sChartError = "用户取消";
            sChartDone = true;
            return;
        }
        try {
            Uri treeUri = data.getData();
            // 持久化读权限，避免重启后失效
            try {
                getContentResolver().takePersistableUriPermission(treeUri,
                        Intent.FLAG_GRANT_READ_URI_PERMISSION);
            } catch (Exception ignored) { }

            int count = CopyChartFolders(treeUri, new File(sPendingDir));
            sChartResult = String.valueOf(count);
        } catch (Exception ex) {
            sChartError = "导入失败: " + ex.getMessage();
        }
        sChartDone = true;
    }

    /** 遍历所选目录：对每个含 maidata.txt 的子目录整体复制到 ChartsRoot。返回导入数量。 */
    private int CopyChartFolders(Uri treeUri, File destRoot) {
        if (!destRoot.exists() && !destRoot.mkdirs()) return 0;
        String rootDocId = DocumentsContract.getTreeDocumentId(treeUri);
        Uri rootChildren = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, rootDocId);
        int count = 0;
        try (Cursor c = getContentResolver().query(rootChildren,
                new String[]{
                        DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                        DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                        DocumentsContract.Document.COLUMN_MIME_TYPE},
                null, null, null)) {
            if (c == null) return 0;
            while (c.moveToNext()) {
                String docId = c.getString(0);
                String name = c.getString(1);
                String mime = c.getString(2);
                if (!DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)) continue;
                if (!DirContains(treeUri, docId, "maidata.txt")) continue;
                File dest = new File(destRoot, Sanitize(name));
                if (dest.exists()) continue;
                if (dest.mkdirs()) {
                    CopyDir(treeUri, docId, dest);
                    count++;
                }
            }
        }
        return count;
    }

    private boolean DirContains(Uri treeUri, String dirDocId, String targetName) {
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, dirDocId);
        try (Cursor c = getContentResolver().query(children,
                new String[]{DocumentsContract.Document.COLUMN_DISPLAY_NAME}, null, null, null)) {
            if (c == null) return false;
            while (c.moveToNext()) {
                if (targetName.equalsIgnoreCase(c.getString(0))) return true;
            }
        }
        return false;
    }

    private void CopyDir(Uri treeUri, String dirDocId, File destDir) {
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, dirDocId);
        try (Cursor c = getContentResolver().query(children,
                new String[]{
                        DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                        DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                        DocumentsContract.Document.COLUMN_MIME_TYPE},
                null, null, null)) {
            if (c == null) return;
            while (c.moveToNext()) {
                String id = c.getString(0);
                String name = c.getString(1);
                String mime = c.getString(2);
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)) {
                    File sub = new File(destDir, Sanitize(name));
                    if (sub.mkdirs()) CopyDir(treeUri, id, sub);
                } else {
                    CopyFile(treeUri, id, new File(destDir, Sanitize(name)));
                }
            }
        }
    }

    private void CopyFile(Uri treeUri, String docId, File dest) {
        try (InputStream in = getContentResolver().openInputStream(
                     DocumentsContract.buildDocumentUriUsingTree(treeUri, docId));
             FileOutputStream fos = new FileOutputStream(dest)) {
            if (in == null) return;
            byte[] buf = new byte[65536];
            int n;
            while ((n = in.read(buf)) > 0) fos.write(buf, 0, n);
        } catch (Exception ignored) { }
    }

    private String GuessExtension(Uri uri) {
        try {
            String name = null;
            try (Cursor c = getContentResolver().query(uri, null, null, null, null)) {
                if (c != null && c.moveToFirst()) {
                    int idx = c.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                    if (idx >= 0) name = c.getString(idx);
                }
            }
            if (name != null && name.contains(".")) {
                String ext = name.substring(name.lastIndexOf('.'));
                if (ext.length() > 1 && ext.length() <= 6 && ext.matches("\\.[a-zA-Z0-9]+"))
                    return ext.toLowerCase();
            }
        } catch (Exception ignored) { }
        String mime = getContentResolver().getType(uri);
        if (mime != null) {
            String ext = MimeTypeMap.getSingleton().getExtensionFromMimeType(mime);
            if (ext != null && !ext.isEmpty()) return "." + ext.toLowerCase();
        }
        return ".mp3";
    }

    private static String Sanitize(String name) {
        if (name == null || name.isEmpty()) return "chart";
        String s = name.replaceAll("[\\\\/:*?\"<>|]", "_").trim();
        if (s.equals(".") || s.equals("..")) s = "_" + s;
        return s.isEmpty() ? "chart" : s;
    }
}
