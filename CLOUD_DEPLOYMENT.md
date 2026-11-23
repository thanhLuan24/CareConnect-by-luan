# Hướng dẫn Triển khai CareConnect lên Google Cloud Run + Neon PostgreSQL

Tài liệu này hướng dẫn từng bước đưa hệ thống **CareConnect** lên kiến trúc Serverless Production với **Google Cloud Run** và **Neon Serverless PostgreSQL** (Phương án 2).

---

## 1. Kiến trúc hệ thống trên Cloud

- **Compute (Backend + Web SPA + AI RAG):** Google Cloud Run (`asia-southeast1` - Singapore), tự động scale từ `0 -> 3` container instances, hỗ trợ HTTPS mặc định và nén Brotli/Gzip.
- **Database:** Neon Serverless PostgreSQL (`ap-southeast-1` - Singapore), tự động kết nối qua SSL (`SslMode=Require`), tích hợp cơ chế `EnableRetryOnFailure` khi DB thức dậy từ trạng thái Scale-to-Zero.
- **Background Worker (`CloudMaintenanceWorker`):**
  - Tự động khởi tạo bảng & seed dữ liệu mẫu (18 tài khoản Bác sĩ, Lễ tân, Dược sĩ, Admin...) lên Neon Postgres ngay lần chạy đầu tiên.
  - Tự động cuốn chiếu lịch trực 14 ngày tiếp theo cho các bác sĩ mỗi 6 giờ.
  - Tự động làm mới cache tin tức y khoa VnExpress + Gemini 2.5 Flash mỗi 20 phút.
- **Bảo mật & Chống Spam:** Rate Limiting `20 requests/phút/IP` cho cụm `/api/ai/*`, chạy container với quyền non-root (`$APP_UID`).

---

## 2. Bước 1: Tạo Database miễn phí trên Neon PostgreSQL

1. Truy cập [https://console.neon.tech](https://console.neon.tech) và đăng nhập (bằng GitHub hoặc Google).
2. Nhấn **New Project**:
   - **Project name:** `careconnect-db`
   - **Region:** Chọn `AWS Asia Pacific (Singapore) - ap-southeast-1` (để cùng khu vực với Cloud Run, giảm độ trễ xuống < 5ms).
3. Sau khi tạo xong, tại màn hình **Connection Details**, sao chép chuỗi **Connection string** (dạng URI):
   ```text
   postgresql://neondb_owner:AbCdEf123456@ep-cool-hill-a1b2c3d4-pooler.ap-southeast-1.aws.neon.tech/neondb?sslmode=require
   ```
   *(Lưu ý: Hệ thống CareConnect đã được tối ưu để đọc trực tiếp chuỗi `postgresql://...` này mà không cần phải tách thủ công sang định dạng `Host=...;Username=...`)*.

---

## 3. Bước 2: Triển khai lên Google Cloud Run (Chỉ với 1 lệnh)

### Cách A: Chạy Script tự động trên Windows PowerShell (Nhanh nhất)

Mở PowerShell tại thư mục `CareConnect-main` và chạy script [deploy-cloudrun.ps1](file:///d:/project/CareConnect-main/CareConnect-main/deploy-cloudrun.ps1):

```powershell
.\deploy-cloudrun.ps1 `
  -ProjectId "YOUR_GCP_PROJECT_ID" `
  -DatabaseUrl "postgresql://neondb_owner:YOUR_PASSWORD@ep-xyz.ap-southeast-1.aws.neon.tech/neondb?sslmode=require" `
  -GeminiApiKey "YOUR_GEMINI_API_KEY"
```

Script sẽ tự động:
1. Sinh khóa bảo mật `Jwt__Key` chuẩn 256-bit ngẫu nhiên.
2. Bật Cloud Run API & Cloud Build API trên Google Cloud.
3. Đóng gói Docker image từ [Dockerfile](file:///d:/project/CareConnect-main/CareConnect-main/Dockerfile) và phát hành lên `asia-southeast1`.

### Cách B: Triển khai qua Google Cloud Console (Giao diện Web)

1. Đẩy code dự án lên GitHub repository của bạn.
2. Vào **Google Cloud Console** -> **Cloud Run** -> **Create Service**.
3. Chọn **Continuously deploy from a repository (Source)** -> Kết nối tới repo GitHub của bạn (Build type: `Dockerfile`).
4. Tại mục **Authentication**: Chọn **Allow unauthenticated invocations**.
5. Tại mục **Container(s), Volumes, Networking, Security** -> tab **Variables & Secrets**, thêm các biến môi trường sau:
   - `DATABASE_URL` = `postgresql://neondb_owner:...@ep-xyz.ap-southeast-1.aws.neon.tech/neondb?sslmode=require`
   - `Jwt__Key` = *(Chuỗi bí mật bất kỳ dài tối thiểu 32 ký tự)*
   - `GEMINI_API_KEY` = *(Khóa Google Gemini API của bạn)*
   - `Gemini__Model` = `gemini-2.5-flash`
   - `EnableSwagger` = `true`
6. Nhấn **Create** và chờ khoảng 2 phút để Google Cloud Run cấp phát đường dẫn `https://careconnect-api-xxxxx-as.a.run.app`.

---

## 4. Bước 3: Kiểm tra sau khi triển khai

Sau khi nhận được URL Cloud Run (ví dụ `https://careconnect-api-xxx.a.run.app`):

1. **Giao diện Web SPA:** Truy cập `https://careconnect-api-xxx.a.run.app`
2. **Kiểm tra trạng thái kết nối Neon Postgres (`/health`):**
   Truy cập `https://careconnect-api-xxx.a.run.app/health`
   Kết quả trả về khi thành công:
   ```json
   {
     "status": "Healthy",
     "provider": "PostgreSQL (Neon Serverless)",
     "databaseConnected": true,
     "timestamp": "2026-11-23T00:15:00Z"
   }
   ```
3. **Tài liệu API Scalar & Swagger:**
   - Scalar Modern UI: `https://careconnect-api-xxx.a.run.app/scalar/v1`
   - Swagger UI: `https://careconnect-api-xxx.a.run.app/swagger`

---

## 5. Bước 4: Kết nối ứng dụng di động Flutter (`mobile_client`) với Cloud Run

File [api_config.dart](file:///d:/project/CareConnect-main/CareConnect-main/mobile_client/lib/config/api_config.dart) đã được nâng cấp để nhận địa chỉ Cloud Run qua tham số `--dart-define`:

```bash
cd mobile_client

# Chạy thử trực tiếp với Backend trên Google Cloud Run
flutter run --dart-define=API_URL=https://careconnect-api-xxx.a.run.app

# Hoặc xuất file cài đặt APK Release kết nối thẳng lên Cloud Run
flutter build apk --release --dart-define=API_URL=https://careconnect-api-xxx.a.run.app
```
