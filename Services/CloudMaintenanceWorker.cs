using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using web.Data;
using web.Models;

namespace web.Services
{
    /// <summary>
    /// Background Worker chuẩn Cloud-Native cho Google Cloud Run và Neon Postgres:
    /// 1. Khởi tạo schema và seed dữ liệu bất đồng bộ ngay sau khi Kestrel mở cổng 8080 (tránh bị Cloud Run Startup Probe timeout).
    /// 2. Tự động cuốn chiếu (rollover) lịch trực 14 ngày tiếp theo cho toàn bộ Bác sĩ mỗi 6 giờ.
    /// 3. Tự động làm mới cache tin tức y khoa VnExpress + Gemini định kỳ mỗi 20 phút.
    /// </summary>
    public class CloudMaintenanceWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CloudMaintenanceWorker> _logger;

        private static readonly string[] StandardSlots =
        {
            "08:00 - 09:00",
            "09:00 - 10:00",
            "10:00 - 11:00",
            "11:00 - 12:00",
            "14:00 - 15:00",
            "15:00 - 16:00"
        };

        public CloudMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<CloudMaintenanceWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[CloudMaintenanceWorker] Starting database initialization and schedule rollover...");

            // Bước 1: Khởi tạo DB & sinh lịch trực 14 ngày ngay khi khởi động
            await InitializeDatabaseAndSchedulesAsync(stoppingToken);

            // Bước 2: Làm nóng cache tin tức y khoa (không chặn luồng)
            await RefreshNewsCacheSafeAsync(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(20));
            int tickCount = 0;

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                tickCount++;
                await RefreshNewsCacheSafeAsync(stoppingToken);

                // Mỗi 6 giờ (18 ticks x 20 phút) kiểm tra và cuốn chiếu lịch trực 14 ngày cho Bác sĩ
                if (tickCount % 18 == 0)
                {
                    await EnsureDoctorSchedulesAsync(stoppingToken);
                }
            }
        }

        private async Task InitializeDatabaseAndSchedulesAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await context.Database.EnsureCreatedAsync(ct);

                // Nếu là SQLite, bật chế độ WAL để tối ưu đọc/ghi đồng thời
                if (context.Database.IsSqlite())
                {
                    await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
                }

                await EnsureDoctorSchedulesInternalAsync(context, ct);
                _logger.LogInformation("[CloudMaintenanceWorker] Database schema and 14-day doctor schedules verified successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CloudMaintenanceWorker] Error during initial database setup and seeding.");
            }
        }

        private async Task EnsureDoctorSchedulesAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await EnsureDoctorSchedulesInternalAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CloudMaintenanceWorker] Error during periodic doctor schedule rollover.");
            }
        }

        private async Task EnsureDoctorSchedulesInternalAsync(AppDbContext context, CancellationToken ct)
        {
            var doctors = await context.Users
                .Where(u => u.Role == "Doctor")
                .Select(u => u.UserId)
                .ToListAsync(ct);

            if (doctors.Count == 0) return;

            var today = DateTime.UtcNow.Date;
            var existingSchedules = await context.DoctorSchedules
                .Where(s => s.AvailableDate >= today)
                .Select(s => new { s.DoctorId, s.AvailableDate, s.TimeSlot })
                .ToListAsync(ct);

            bool hasNewSchedules = false;
            for (int i = 0; i < 14; i++)
            {
                var date = today.AddDays(i);
                foreach (var docId in doctors)
                {
                    foreach (var slot in StandardSlots)
                    {
                        bool exists = existingSchedules.Any(s =>
                            s.DoctorId == docId &&
                            s.AvailableDate.Date == date.Date &&
                            s.TimeSlot == slot);

                        if (!exists)
                        {
                            context.DoctorSchedules.Add(new DoctorSchedule
                            {
                                DoctorId = docId,
                                AvailableDate = date,
                                TimeSlot = slot
                            });
                            hasNewSchedules = true;
                        }
                    }
                }
            }

            if (hasNewSchedules)
            {
                await context.SaveChangesAsync(ct);
            }
        }

        private async Task RefreshNewsCacheSafeAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<IAiService>();
                await aiService.GetTrendingNewsAsync(forceRefresh: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CloudMaintenanceWorker] Non-fatal warning while warming up medical news cache.");
            }
        }
    }
}
