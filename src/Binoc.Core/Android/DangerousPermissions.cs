namespace Binoc.Core.Android;

/// <summary>
/// The set of Android permissions binoc treats as high-signal: the runtime "dangerous" permission groups
/// plus the special/app-ops permissions that grant broad reach (overlay, install packages, all-files
/// access, package visibility, accessibility). Matched on the short name (the part after the final dot), so
/// both <c>android.permission.CAMERA</c> and a vendor-namespaced variant resolve.
/// </summary>
public static class DangerousPermissions
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        // Location
        "ACCESS_FINE_LOCATION", "ACCESS_COARSE_LOCATION", "ACCESS_BACKGROUND_LOCATION", "ACCESS_MEDIA_LOCATION",
        // Camera / mic
        "CAMERA", "RECORD_AUDIO",
        // Contacts / accounts / calendar
        "READ_CONTACTS", "WRITE_CONTACTS", "GET_ACCOUNTS", "READ_CALENDAR", "WRITE_CALENDAR",
        // Phone / call log / SMS
        "READ_PHONE_STATE", "READ_PHONE_NUMBERS", "CALL_PHONE", "ANSWER_PHONE_CALLS", "ADD_VOICEMAIL",
        "USE_SIP", "PROCESS_OUTGOING_CALLS", "READ_CALL_LOG", "WRITE_CALL_LOG",
        "SEND_SMS", "RECEIVE_SMS", "READ_SMS", "RECEIVE_WAP_PUSH", "RECEIVE_MMS",
        // Storage / media
        "READ_EXTERNAL_STORAGE", "WRITE_EXTERNAL_STORAGE", "READ_MEDIA_IMAGES", "READ_MEDIA_VIDEO",
        "READ_MEDIA_AUDIO", "READ_MEDIA_VISUAL_USER_SELECTED",
        // Sensors / activity
        "BODY_SENSORS", "BODY_SENSORS_BACKGROUND", "ACTIVITY_RECOGNITION",
        // Nearby / bluetooth
        "NEARBY_WIFI_DEVICES", "BLUETOOTH_SCAN", "BLUETOOTH_CONNECT", "BLUETOOTH_ADVERTISE", "UWB_RANGING",
        // Notifications
        "POST_NOTIFICATIONS",
        // Special / app-ops — broad reach
        "SYSTEM_ALERT_WINDOW", "REQUEST_INSTALL_PACKAGES", "MANAGE_EXTERNAL_STORAGE", "QUERY_ALL_PACKAGES",
        "WRITE_SETTINGS", "PACKAGE_USAGE_STATS", "BIND_ACCESSIBILITY_SERVICE", "BIND_DEVICE_ADMIN",
        "MANAGE_MEDIA", "SCHEDULE_EXACT_ALARM",
    };

    public static bool IsDangerous(string permission)
    {
        int dot = permission.LastIndexOf('.');
        var shortName = dot >= 0 ? permission[(dot + 1)..] : permission;
        return Names.Contains(shortName);
    }
}
