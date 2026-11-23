import 'package:flutter/foundation.dart' show kIsWeb, defaultTargetPlatform;
import 'package:flutter/material.dart' show TargetPlatform;

class ApiConfig {
  // Hỗ trợ truyền URL Cloud Run khi build:
  // flutter build apk --dart-define=API_URL=https://careconnect-api-xxx.a.run.app
  static const String _cloudRunUrl = String.fromEnvironment('API_URL', defaultValue: '');

  // Đường dẫn Web/API cho điện thoại thật cắm cáp USB (sử dụng lệnh adb reverse)
  static const String _androidPhysicalUrl = "http://127.0.0.1:5164";
  // Đường dẫn Web/API cho máy ảo Android Emulator
  static const String _androidEmulatorUrl = "http://10.0.2.2:5164";
  static const String _defaultUrl = "http://localhost:5164";

  static String get webUrl {
    if (_cloudRunUrl.isNotEmpty) {
      return _cloudRunUrl.endsWith('/')
          ? _cloudRunUrl.substring(0, _cloudRunUrl.length - 1)
          : _cloudRunUrl;
    }
    if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android) {
      // Đang dùng điện thoại thật cắm cáp USB (đã cấu hình adb reverse)
      // Nếu chuyển sang dùng máy ảo, hãy đổi thành _androidEmulatorUrl;
      return _androidPhysicalUrl;
    }
    return _defaultUrl;
  }

  static String get baseUrl {
    return "$webUrl/api";
  }

  // Khóa Sentry DSN cho ứng dụng Flutter Mobile (có thể truyền qua --dart-define=SENTRY_DSN=...)
  static const String sentryDsn = String.fromEnvironment(
    'SENTRY_DSN',
    defaultValue: "https://examplePublicKey@o0.ingest.sentry.io/0",
  );
}
