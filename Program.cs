using System;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Scalar.AspNetCore;
using web.Data;
using web.Services;

// Bật tương thích kiểu DateTime linh hoạt cho PostgreSQL (Neon Postgres)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Hỗ trợ cổng động từ biến môi trường PORT của Google Cloud Run
var cloudRunPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(cloudRunPort) && int.TryParse(cloudRunPort, out int parsedPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
}

// Chỉ kích hoạt Sentry khi có cấu hình DSN hợp lệ (tránh cảnh báo thừa trên Cloud Run)
var sentryDsn = builder.Configuration["Sentry:Dsn"] ?? Environment.GetEnvironmentVariable("SENTRY_DSN");
if (!string.IsNullOrWhiteSpace(sentryDsn) && !sentryDsn.Contains("examplePublicKey"))
{
    builder.WebHost.UseSentry(o => o.Dsn = sentryDsn);
}

// 1. Cấu hình Forwarded Headers cho Google Cloud Run Load Balancer / Reverse Proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// 2. Bật nén phản hồi (Brotli & Gzip) giúp giảm ~80% dung lượng tải SPA (wwwroot) trên Cloud
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/javascript",
        "text/css",
        "text/html",
        "application/json",
        "image/svg+xml"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

// 3. Cấu hình Rate Limiting chống spam Gemini AI Quota trên môi trường Cloud công cộng
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AiRateLimit", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2
        });
    });
});

// Add services to the container.
builder.Services.AddControllers();

// 4. Đăng ký dịch vụ CORS hỗ trợ di động và tên miền Cloud Run
var allowedOriginsRaw = builder.Configuration["Cors:AllowedOrigins"] ?? Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? "*";
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileCorsPolicy", policy =>
    {
        if (allowedOriginsRaw == "*" || string.IsNullOrWhiteSpace(allowedOriginsRaw))
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            var origins = allowedOriginsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// 5. Tự động nhận diện và cấu hình Database: Neon PostgreSQL (Cloud Run) hoặc SQLite (Local Dev Fallback)
var rawConnStr = Environment.GetEnvironmentVariable("DATABASE_URL")
                 ?? Environment.GetEnvironmentVariable("NEON_DATABASE_URL")
                 ?? builder.Configuration.GetConnectionString("DefaultConnection")
                 ?? "Data Source=hospital.db";

bool isPostgres = IsPostgresConnectionString(rawConnStr);
string resolvedConnStr = isPostgres ? NormalizePostgresConnectionString(rawConnStr) : rawConnStr;

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (isPostgres)
    {
        options.UseNpgsql(resolvedConnStr, npgsqlOptions =>
        {
            // Tự động thử lại tối đa 3 lần khi Neon Serverless Postgres đang thức dậy từ trạng thái Scale-to-Zero
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
        });
    }
    else
    {
        options.UseSqlite(resolvedConnStr);
    }
});

// Đăng ký các Services nghiệp vụ
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IRagService, RagService>();
builder.Services.AddHttpClient<IAiService, AiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IMedicalRecordService, MedicalRecordService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Đăng ký Background Worker tự động khởi tạo DB, cuốn chiếu lịch trực 14 ngày và cập nhật tin tức
builder.Services.AddHostedService<CloudMaintenanceWorker>();

// 6. Cấu hình JWT Bearer Authentication
var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
             ?? builder.Configuration["Jwt:Key"];

if (string.IsNullOrEmpty(jwtKey) || jwtKey == "SuperSecretJWTKeyForHospitalSystem2026!")
{
    if (!builder.Environment.IsDevelopment())
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=================================================================================");
        Console.WriteLine("[CẢNH BÁO CLOUD RUN] Bạn chưa truyền biến môi trường Jwt__Key hoặc JWT_SECRET_KEY.");
        Console.WriteLine("Hệ thống tạm thời dùng khóa dự phòng để container không bị crash khi khởi động.");
        Console.WriteLine("Hãy cấu hình biến môi trường Jwt__Key trong Google Cloud Run Variables & Secrets.");
        Console.WriteLine("=================================================================================");
        Console.ResetColor();
    }
    jwtKey = "SuperSecretJWTKeyForHospitalSystem2026!";
}
var key = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Cloud Run kết thúc TLS ở tầng Load Balancer
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "HospitalApiIssuer",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "HospitalApiAudience",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// 7. Cấu hình Swagger/OpenAPI & Scalar
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CareConnect High-Tech API Reference (Master)",
        Version = "v1",
        Description = "Toàn bộ danh sách API tích hợp của hệ thống phòng khám CareConnect (Cloud Run + Neon Postgres)."
    });
    options.SwaggerDoc("auth", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CareConnect Authentication & Profile API",
        Version = "v1",
        Description = "Các API phục vụ xác thực đăng nhập, đăng ký tài khoản, xem/sửa hồ sơ y tế cá nhân và đánh giá phản hồi bệnh nhân."
    });
    options.SwaggerDoc("appointments", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CareConnect Clinic & Appointment Booking API",
        Version = "v1",
        Description = "Các API cốt lõi quản lý thông tin chi nhánh phòng khám, danh sách ca trực bác sĩ, quy trình đặt lịch hẹn khám và hồ sơ bệnh án."
    });
    options.SwaggerDoc("ai", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CareConnect Intelligent AI Assistant API",
        Version = "v1",
        Description = "Các API phục vụ chatbot trợ lý thông minh, phân tích triệu chứng sơ bộ và tin tức y khoa cập nhật liên tục 24/7."
    });
    options.SwaggerDoc("admin", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CareConnect Clinic Administration API",
        Version = "v1",
        Description = "Các API quản lý vận hành phòng khám chuyên dụng dành riêng cho Quản trị viên (Admin) và Lễ tân (Receptionist)."
    });

    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        if (docName == "v1") return true;
        return apiDesc.GroupName == docName;
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Nhập theo định dạng: Bearer [your_jwt_token]"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (System.IO.File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Kích hoạt Forwarded Headers và Nén phản hồi đầu tiên trong Pipeline
app.UseForwardedHeaders();
app.UseResponseCompression();

// Cho phép bật Swagger & Scalar trên Cloud Run thông qua cờ EnableSwagger
bool enableSwagger = app.Environment.IsDevelopment() ||
                     builder.Configuration.GetValue<bool>("EnableSwagger", true);

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("CareConnect High-Tech API Reference")
            .WithTheme(ScalarTheme.DeepSpace)
            .WithLayout(ScalarLayout.Modern)
            .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");

        options.CustomCss = @"
            .references-header {
                position: sticky !important;
                top: 0 !important;
                z-index: 1000 !important;
                background: #09090b !important;
                border-bottom: 1px solid #27272a !important;
            }
        ";
    });
}

app.UseCors("MobileCorsPolicy");

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var fileName = ctx.File.Name.ToLowerInvariant();
        if (fileName.EndsWith(".html"))
        {
            // Luôn kiểm tra bản mới nhất đối với index.html
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
        }
        else
        {
            // Cho phép cache tài nguyên tĩnh (CSS, JS, Hình ảnh) trong 1 giờ để tăng tốc tải trang trên Cloud
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=3600";
        }

        var contentType = ctx.Context.Response.ContentType;
        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.StartsWith("text/html") ||
                contentType.StartsWith("text/css") ||
                contentType.StartsWith("application/javascript") ||
                contentType.StartsWith("text/javascript"))
            {
                if (!contentType.Contains("charset"))
                {
                    ctx.Context.Response.ContentType = contentType + "; charset=utf-8";
                }
            }
        }
    }
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Endpoint Health Check chuẩn cho Google Cloud Run Startup & Liveness Probe
app.MapGet("/health", async (AppDbContext db) =>
{
    bool dbOk = false;
    try
    {
        dbOk = await db.Database.CanConnectAsync();
    }
    catch
    {
        dbOk = false;
    }

    return Results.Ok(new
    {
        status = dbOk ? "Healthy" : "Degraded",
        provider = isPostgres ? "PostgreSQL (Neon Serverless)" : "SQLite (Local)",
        databaseConnected = dbOk,
        timestamp = DateTime.UtcNow
    });
}).AllowAnonymous();

app.MapControllers();

app.Run();

// =====================================================================
// Helper nhận diện & chuẩn hóa chuỗi kết nối Neon PostgreSQL URI / ADO.NET
// =====================================================================
static bool IsPostgresConnectionString(string conn)
{
    if (string.IsNullOrWhiteSpace(conn)) return false;
    var trimmed = conn.Trim();
    return trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
           trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
           trimmed.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
           trimmed.Contains("neon.tech", StringComparison.OrdinalIgnoreCase);
}

static string NormalizePostgresConnectionString(string conn)
{
    var trimmed = conn.Trim();
    if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return trimmed;
    }

    // Chuyển đổi trực tiếp chuỗi URI từ Neon Dashboard:
    // postgresql://user:password@ep-xyz.region.aws.neon.tech/neondb?sslmode=require
    var uri = new Uri(trimmed);
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    var database = uri.AbsolutePath.TrimStart('/');

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = string.IsNullOrEmpty(database) ? "neondb" : database,
        Username = username,
        Password = password,
        SslMode = SslMode.Require,
        Pooling = true,
        MaxPoolSize = 20,
        Timeout = 15,
        CommandTimeout = 30
    };

    return builder.ToString();
}
