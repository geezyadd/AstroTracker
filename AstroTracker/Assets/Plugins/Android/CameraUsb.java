package com.astrotracker;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.hardware.usb.UsbConstants;
import android.hardware.usb.UsbDevice;
import android.hardware.usb.UsbDeviceConnection;
import android.hardware.usb.UsbManager;
import android.mtp.MtpConstants;
import android.mtp.MtpDevice;
import android.mtp.MtpObjectInfo;
import android.os.Build;

import java.io.File;
import java.io.FileOutputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Reads photos from a camera connected by USB (PTP/MTP), for Unity.
 * Every call opens the camera, does its work and closes it again, so the camera is free in between.
 */
public final class CameraUsb {

    public interface Callback {
        void onResult(String data);

        /** One of: no_camera, permission_denied, open_failed, no_photos, download_failed, error. */
        void onFailed(String reason);
    }

    private interface Job {
        String run(Context context, MtpDevice camera) throws Exception;
    }

    private static final class Failure extends Exception {
        Failure(String reason) {
            super(reason);
        }
    }

    private static final String ACTION_PERMISSION = "com.astrotracker.USB_PERMISSION";
    private static final String THUMBNAIL_FOLDER = "camera_thumbnails";
    private static final String DOWNLOAD_FILE = "camera_frame.jpg";
    private static final int CANON_VENDOR_ID = 0x04A9;
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();

    private CameraUsb() {
    }

    /**
     * Lists the newest JPG photos, newest first. The result has one line per photo:
     * handle, name, time taken in milliseconds, size in bytes and thumbnail file path (may be empty),
     * separated by tabs.
     */
    public static void listPhotos(Activity activity, final int limit, Callback callback) {
        withCamera(activity, callback, new Job() {
            @Override
            public String run(Context context, MtpDevice camera) throws Exception {
                return describePhotos(context, camera, limit);
            }
        });
    }

    /** Copies one photo into the app cache; the result is the path of the copy. */
    public static void download(Activity activity, final int handle, Callback callback) {
        withCamera(activity, callback, new Job() {
            @Override
            public String run(Context context, MtpDevice camera) throws Exception {
                File file = new File(context.getCacheDir(), DOWNLOAD_FILE);
                file.delete();
                if (!camera.importFile(handle, file.getAbsolutePath())) {
                    throw new Failure("download_failed");
                }
                return file.getAbsolutePath();
            }
        });
    }

    private static void withCamera(Activity activity, final Callback callback, final Job job) {
        final Context context = activity.getApplicationContext();
        final UsbManager usb = (UsbManager) context.getSystemService(Context.USB_SERVICE);
        final UsbDevice device = usb == null ? null : findCamera(usb);
        if (device == null) {
            callback.onFailed("no_camera");
            return;
        }

        final Runnable start = new Runnable() {
            @Override
            public void run() {
                WORKER.execute(new Runnable() {
                    @Override
                    public void run() {
                        runJob(context, usb, device, callback, job);
                    }
                });
            }
        };
        if (usb.hasPermission(device)) {
            start.run();
            return;
        }

        // The system shows its own dialog and broadcasts the answer.
        BroadcastReceiver receiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context receiverContext, Intent intent) {
                context.unregisterReceiver(this);
                if (usb.hasPermission(device)) {
                    start.run();
                } else {
                    callback.onFailed("permission_denied");
                }
            }
        };
        IntentFilter filter = new IntentFilter(ACTION_PERMISSION);
        if (Build.VERSION.SDK_INT >= 33) {
            context.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED);
        } else {
            context.registerReceiver(receiver, filter);
        }
        Intent intent = new Intent(ACTION_PERMISSION).setPackage(context.getPackageName());
        int flags = Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0;
        usb.requestPermission(device, PendingIntent.getBroadcast(context, 0, intent, flags));
    }

    private static void runJob(Context context, UsbManager usb, UsbDevice device, Callback callback, Job job) {
        UsbDeviceConnection connection = usb.openDevice(device);
        if (connection == null) {
            callback.onFailed("open_failed");
            return;
        }
        MtpDevice camera = new MtpDevice(device);
        if (!camera.open(connection)) {
            connection.close();
            callback.onFailed("open_failed");
            return;
        }

        String result = null;
        String failure = null;
        try {
            result = job.run(context, camera);
        } catch (Failure e) {
            failure = e.getMessage();
        } catch (Exception e) {
            failure = "error";
        } finally {
            camera.close();
        }
        if (failure != null) {
            callback.onFailed(failure);
        } else {
            callback.onResult(result);
        }
    }

    /** A device with a still-image (PTP) interface; a Canon device is accepted even without one. */
    private static UsbDevice findCamera(UsbManager usb) {
        UsbDevice canon = null;
        for (UsbDevice device : usb.getDeviceList().values()) {
            for (int i = 0; i < device.getInterfaceCount(); i++) {
                if (device.getInterface(i).getInterfaceClass() == UsbConstants.USB_CLASS_STILL_IMAGE) {
                    return device;
                }
            }
            if (device.getVendorId() == CANON_VENDOR_ID) {
                canon = device;
            }
        }
        return canon;
    }

    private static String describePhotos(Context context, MtpDevice camera, int limit) throws Exception {
        // Handles grow with every shot, so the newest photos are at the end of each list.
        List<Integer> handles = new ArrayList<Integer>();
        int[] storages = camera.getStorageIds();
        if (storages != null) {
            for (int storage : storages) {
                int[] found = camera.getObjectHandles(storage, MtpConstants.FORMAT_EXIF_JPEG, 0);
                if (found == null || found.length == 0) {
                    // Some cameras ignore the format filter; take everything and sort it out below.
                    found = camera.getObjectHandles(storage, 0, 0);
                }
                if (found != null) {
                    for (int handle : found) {
                        handles.add(handle);
                    }
                }
            }
        }

        File folder = new File(context.getCacheDir(), THUMBNAIL_FOLDER);
        File[] old = folder.listFiles();
        if (old != null) {
            for (File file : old) {
                file.delete();
            }
        }
        folder.mkdirs();

        final List<MtpObjectInfo> photos = new ArrayList<MtpObjectInfo>();
        // RAW files and folders share the list; look at a bounded number of entries from the end.
        int oldest = Math.max(0, handles.size() - limit * 4);
        for (int i = handles.size() - 1; i >= oldest && photos.size() < limit; i--) {
            MtpObjectInfo info = camera.getObjectInfo(handles.get(i));
            if (info == null || info.getName() == null) {
                continue;
            }
            String name = info.getName().toLowerCase(Locale.ROOT);
            if (name.endsWith(".jpg") || name.endsWith(".jpeg")) {
                photos.add(info);
            }
        }
        if (photos.isEmpty()) {
            throw new Failure("no_photos");
        }
        Collections.sort(photos, new Comparator<MtpObjectInfo>() {
            @Override
            public int compare(MtpObjectInfo a, MtpObjectInfo b) {
                if (a.getDateCreated() != b.getDateCreated()) {
                    return a.getDateCreated() < b.getDateCreated() ? 1 : -1;
                }
                return b.getObjectHandle() - a.getObjectHandle();
            }
        });

        StringBuilder lines = new StringBuilder();
        for (MtpObjectInfo info : photos) {
            String thumbnail = "";
            byte[] image = camera.getThumbnail(info.getObjectHandle());
            if (image != null && image.length > 0) {
                File file = new File(folder, info.getObjectHandle() + ".jpg");
                FileOutputStream out = new FileOutputStream(file);
                try {
                    out.write(image);
                } finally {
                    out.close();
                }
                thumbnail = file.getAbsolutePath();
            }
            lines.append(info.getObjectHandle()).append('\t')
                    .append(info.getName().replace('\t', ' ')).append('\t')
                    .append(info.getDateCreated()).append('\t')
                    .append(info.getCompressedSize() & 0xFFFFFFFFL).append('\t')
                    .append(thumbnail).append('\n');
        }
        return lines.toString();
    }
}
