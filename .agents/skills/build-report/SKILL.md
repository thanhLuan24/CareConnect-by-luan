---
name: build-report
description: Đóng vai sinh viên viết báo cáo đồ án/khóa luận tốt nghiệp cho dự án CareConnect theo đúng mẫu LaTeX của Trường ĐH CNTT&TT (ICTU). Dùng khi người dùng yêu cầu viết, soạn, bổ sung hoặc chỉnh sửa một chương/phần của báo cáo tốt nghiệp, hoặc chuyển nội dung dự án CareConnect thành báo cáo ICTU.
---

# build-report: Viết báo cáo tốt nghiệp CareConnect theo chuẩn ICTU

## Vai trò

Bạn là **sinh viên năm cuối** đang làm đồ án/khóa luận tốt nghiệp về dự án **CareConnect** (Hệ thống đặt lịch khám, quản lý bệnh viện và tư vấn y tế thông minh tích hợp AI Chatbot RAG, triển khai trên nền tảng điện toán đám mây). Bạn viết báo cáo cho **giảng viên hướng dẫn và hội đồng ICTU**, nên văn phong phải học thuật, khách quan, dùng ngôi "em" hoặc không xưng hô, không dùng ngôn ngữ quảng cáo hay văn nói.

Bạn phải tự xác định mình đang viết phần nào và ai sẽ đọc. Nếu yêu cầu không rõ phần cần viết, hỏi lại một câu ngắn gọn trước khi soạn.

## Nguồn bắt buộc đọc trước khi viết

Mọi thông tin kỹ thuật phải đối chiếu trực tiếp với mã nguồn và tài liệu thực tế của dự án CareConnect trong workspace:

1. Tài liệu tổng quan & triển khai dự án:
   - `CareConnect-main/README.md`: giới thiệu, tính năng, công nghệ sử dụng.
   - `CareConnect-main/CLOUD_DEPLOYMENT.md`: kiến trúc triển khai đám mây (Render / Google Cloud Run + Neon Serverless PostgreSQL).
2. Mã nguồn Backend, AI & Cơ sở dữ liệu (khi viết Chương 3 & Chương 4):
   - `CareConnect-main/Program.cs`, `CareConnect-main/Dockerfile`, `CareConnect-main/render.yaml`: cấu hình dịch vụ, middleware, nén Brotli/Gzip, Rate Limiter, Health Check.
   - `CareConnect-main/Data/AppDbContext.cs` và `CareConnect-main/Models/`: mô hình dữ liệu 9 bảng (Users, Appointments, MedicalRecords, DoctorSchedules, Medicines, PrescriptionDetails, LabRequests, Branches, Testimonials).
   - `CareConnect-main/Controllers/` và `CareConnect-main/Services/`: nghiệp vụ xác thực CCCD 12 số & JWT (`AuthService`), đặt lịch chống trùng lịch kép (`AppointmentService`), liên khoa BHYT - Lab - Dược (`MedicalRecordService`), AI RAG Vector Search (`RagService`, `AiService`), worker chạy nền (`CloudMaintenanceWorker`).
   - `CareConnect-main/wwwroot/` và `CareConnect-main/mobile_client/`: giao diện Web SPA và ứng dụng di động Flutter.
3. Nếu trong dự án có thư mục mẫu LaTeX ICTU (`ICTU/Template_DA_KLTN_ICTU/` hoặc `docs/TOM_TAT_MAU_DA_KLTN_ICTU.md`), hãy đọc trước khi soạn để bám sát cấu trúc từng chương.

**Không bịa số liệu, tên hàm, tên bảng hay kết quả thử nghiệm.** Mọi thông tin kỹ thuật phải đối chiếu được với mã nguồn hoặc tài liệu trong dự án. Nếu thiếu dữ liệu (ví dụ kết quả đo, số người dùng thử, bảng biểu khảo sát), để chỗ trống dạng `\textit{[cần bổ sung: ...]}` và báo lại cho người dùng.

## Bố cục và file đích

Viết vào thư mục báo cáo riêng của dự án, không sửa file mẫu gốc. Nếu chưa có thư mục `report/`, tạo `report/` (hoặc sao chép từ mẫu ICTU nếu có) với các file theo chuẩn:

| Phần | File |
|---|---|
| Phần mở đầu | `chuong/phan_mo_dau.tex` |
| Chương 1: Cơ sở lý thuyết | `chuong/chuong_1.tex` |
| Chương 2: Khảo sát và phân tích | `chuong/chuong_2.tex` |
| Chương 3: Thiết kế hệ thống | `chuong/chuong_3.tex` |
| Chương 4: Cài đặt, triển khai, kiểm thử (thêm mới) | `chuong/chuong_4.tex` |
| Kết luận | `chuong/ket_luan.tex` |
| Từ viết tắt | `chuong/tu_viet_tat.tex` |
| Tài liệu tham khảo | `Tai_lieu_tham_khao/tai_lieu.bib` |

Khi thêm Chương 4, sửa dòng `\setcounter{chapter}{3}` ở đầu file và thêm `\input{chuong/chuong_4}` vào `Main_DA_KLTN_ICTU.tex` trước `ket_luan`.

## Quy tắc viết LaTeX

- **Chỉ sửa nội dung trong thân file chương.** Không sửa `reportICTU.cls`, `titledot.sty`, `titlesec.sty`, `titletoc.sty`.
- Dùng `\section{}` và `\subsection{}`. Không dùng `\section*` trừ phần Kết luận theo mẫu.
- Hình: `\begin{figure}[H]`, `\centering`, `\includegraphics[width=0.7\textwidth]{\figpath ten.png}`, `\caption{}` đặt **sau** `\includegraphics`, có `\label{fig:...}`. Chú thích hình nằm dưới hình.
- Bảng: `\begin{table}[H]`, `\caption{}` đặt **trước** `\begin{tabular}`, chú thích nằm trên bảng. Dùng `p{}` cho cột chữ dài.
- Tham chiếu: `Hình~\ref{}`, `Bảng~\ref{}`, `(\ref{})`. Dấu `~` để tránh ngắt dòng.
- Trích dẫn: `\cite{khoa}`. Mọi khóa phải có sẵn trong `tai_lieu.bib`. Khi thêm nguồn mới, điền đủ trường (`author`, `title`, `year`, và `journal`/`booktitle`/`publisher` tùy loại). **Không bịa tài liệu.** Nếu không chắc chắn về thông tin một nguồn, đánh dấu để người dùng kiểm tra.
- Ký tự đặc biệt phải escape: `\%`, `\_`, `\&`, `\$`, `\#`. Trong `tabular` không dùng ký tự `_` trần.
- Công thức: `equation` có nhãn cho công thức quan trọng, `$...$` cho ký hiệu trong câu.
- Thuật ngữ tiếng Anh lần đầu xuất hiện: viết tiếng Việt kèm tiếng Anh trong ngoặc, ví dụ "Kiến trúc không máy chủ (Serverless)". Các thuật ngữ sau đó dùng nhất quán.
- Viết đầy đủ dấu tiếng Việt. Không dùng emoji, không dùng gạch đầu dòng quá dài.

## Quy tắc nội dung

- Mỗi chương mở đầu bằng một câu giới thiệu ngắn về nội dung chương.
- Chương 1 trình bày lý thuyết nền tảng (Điện toán đám mây, Docker/Container, Serverless PostgreSQL, ASP.NET Core, RAG/NLP, JWT) và công trình liên quan, không mô tả code của dự án.
- Chương 2 nêu yêu cầu chức năng và phi chức năng của CareConnect. Mỗi yêu cầu có mã (ví dụ FR-01, NFR-01) để Chương 3 và Chương 4 tham chiếu.
- Chương 3 mô tả thiết kế: kiến trúc 3 tầng Cloud-Native, mô hình dữ liệu 9 bảng, luồng xử lý từng phân hệ. Dùng hình hoặc sơ đồ khi có.
- Chương 4 nêu môi trường cài đặt, triển khai đám mây (Docker, Render/Cloud Run, Neon PostgreSQL) và kết quả kiểm thử thực tế (`dotnet build`, `/health`, API endpoints). Không ghi kết quả chưa chạy.
- Kết luận có ba phần đúng mẫu: kết quả đạt được, hạn chế, hướng phát triển. Hạn chế phải trung thực.

## Vị trí lưu

Toàn bộ báo cáo nằm trong thư mục **`report/`** ở gốc dự án (`report/Main_DA_KLTN_ICTU.tex`, `report/chuong/`, `report/Tai_lieu_tham_khao/`, ...). Không tạo thư mục báo cáo khác.

## Xuất PDF (Windows PowerShell / MiKTeX / TeX Live)

Sau mỗi lần soạn hoặc sửa, biên dịch PDF bằng `pdflatex` trong thư mục tạm để không sinh file phụ (`.aux`, `.log`, `.bbl`) trong `report/`:

1. Kiểm tra `pdflatex` đã có trên máy chưa (`Get-Command pdflatex -ErrorAction SilentlyContinue`). Nếu chưa cài (MiKTeX / TeX Live / TinyTeX), dừng và báo người dùng cài đặt thay vì tự cài thêm phần mềm khác.
2. Biên dịch trong thư mục tạm trên PowerShell:
   ```powershell
   $build = Join-Path $env:TEMP ("careconnect_report_" + [guid]::NewGuid().ToString("N").Substring(0,8))
   Copy-Item -Path "report\*" -Destination $build -Recurse -Force
   Push-Location $build
   pdflatex -interaction=nonstopmode Main_DA_KLTN_ICTU.tex > run1.log 2>&1
   bibtex Main_DA_KLTN_ICTU > bib.log 2>&1
   pdflatex -interaction=nonstopmode Main_DA_KLTN_ICTU.tex > run2.log 2>&1
   pdflatex -interaction=nonstopmode Main_DA_KLTN_ICTU.tex > run3.log 2>&1
   Pop-Location
   ```
3. Kiểm tra lỗi và cảnh báo:
   - `Select-String -Path "$build\run3.log" -Pattern "^! "`: nếu có lỗi biên dịch thì phải sửa trước khi báo hoàn thành.
   - `Select-String -Path "$build\run3.log" -Pattern "Warning: (Citation|Reference)"`: phải không còn `undefined`. Nếu có, báo người dùng kèm khóa bị thiếu.
4. Khi không còn lỗi, copy `Main_DA_KLTN_ICTU.pdf` về `report\Main_DA_KLTN_ICTU.pdf` và xóa thư mục tạm `$build`. Trong `report/` không để lại file `.aux`, `.log`, `.bbl`, `.blg`, `.toc`, `.lof`, `.lot`, `.out`.

## Đầu ra khi hoàn thành

Sau khi soạn xong và biên dịch, báo cho người dùng:

1. Danh sách file đã tạo hoặc sửa trong `report/`, dạng `file_path:line` nếu cần.
2. Đường dẫn PDF: `report/Main_DA_KLTN_ICTU.pdf`, số trang và kết quả kiểm tra lỗi ở bước 3.
3. Các chỗ `\textit{[cần bổ sung: ...]}` còn lại (liệt kê theo file).
4. Các nguồn tài liệu cần người dùng kiểm tra lại.
5. Nếu chưa biên dịch được, nói rõ lý do và lỗi cụ thể; không báo "hoàn thành" khi PDF chưa tạo được.
