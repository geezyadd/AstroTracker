package com.astrotracker;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/** Opens the system file picker and hands the chosen image to Unity as a file in the app cache. */
public class ImagePickerFragment extends Fragment {

    public interface Callback {
        /** The path is empty when the user left the picker without choosing. */
        void onPicked(String path, String name);

        void onFailed(String message);
    }

    private static final int REQUEST_CODE = 7461;
    private static final String CACHE_FILE = "picked_frame";

    private Callback callback;

    public static void pick(final Activity activity, final Callback callback) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                ImagePickerFragment fragment = new ImagePickerFragment();
                fragment.callback = callback;
                activity.getFragmentManager().beginTransaction().add(fragment, "AstroTrackerImagePicker").commit();
            }
        });
    }

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (callback == null) {
            // Recreated by the system after the app was killed: nobody is waiting for the result.
            close();
            return;
        }

        Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("image/*");
        try {
            startActivityForResult(intent, REQUEST_CODE);
        } catch (Exception e) {
            close();
            callback.onFailed(String.valueOf(e.getMessage()));
        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode != REQUEST_CODE) {
            return;
        }
        final Activity activity = getActivity();
        final Callback target = callback;
        close();
        if (target == null) {
            return;
        }
        if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) {
            target.onPicked("", "");
            return;
        }

        final Uri uri = data.getData();
        new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    File file = new File(activity.getCacheDir(), CACHE_FILE);
                    copy(activity, uri, file);
                    target.onPicked(file.getAbsolutePath(), displayName(activity, uri));
                } catch (Exception e) {
                    target.onFailed(String.valueOf(e.getMessage()));
                }
            }
        }).start();
    }

    private void close() {
        getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
    }

    private static void copy(Activity activity, Uri uri, File file) throws Exception {
        InputStream in = activity.getContentResolver().openInputStream(uri);
        if (in == null) {
            throw new Exception("Cannot open the selected file");
        }
        try {
            OutputStream out = new FileOutputStream(file);
            try {
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = in.read(buffer)) > 0) {
                    out.write(buffer, 0, read);
                }
            } finally {
                out.close();
            }
        } finally {
            in.close();
        }
    }

    private static String displayName(Activity activity, Uri uri) {
        Cursor cursor = activity.getContentResolver().query(uri, null, null, null, null);
        if (cursor == null) {
            return "";
        }
        try {
            int column = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
            if (column >= 0 && cursor.moveToFirst()) {
                String name = cursor.getString(column);
                return name == null ? "" : name;
            }
            return "";
        } finally {
            cursor.close();
        }
    }
}
