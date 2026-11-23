<#
.SYNOPSIS
    Script tự động triển khai CareConnect lên Google Cloud Run + Neon PostgreSQL.
.EXAMPLE
    .\deploy-cloudrun.ps1 -ProjectId "careconnect-prod-123" -DatabaseUrl "postgresql://neondb_owner:...@ep-xyz.ap-southeast-1.aws.neon.tech/neondb?sslmode=require" -GeminiApiKey "AIzaSy..."
#>

param(
    [Parameter(Mandatory=$true, HelpMessage="Google Cloud Project ID của bạn")]
    [string]$ProjectId,

    [Parameter(Mandatory=$true, HelpMessage="Chuỗi kết nối Neon PostgreSQL (postgresql://...)")]
    [string]$DatabaseUrl,

    [Parameter(Mandatory=$false, HelpMessage="Google Gemini API Key")]
    [string]$GeminiApiKey = "",

    [Parameter(Mandatory=$false, HelpMessage="Khóa bí mật JWT (tối thiểu 32 ký tự)")]
    [string]$JwtSecret = "",

    [Parameter(Mandatory=$false, HelpMessage="Khu vực triển khai Cloud Run (Mặc định: asia-southeast1 - Singapore)")]
    [string]$Region = "asia-southeast1",

    [Parameter(Mandatory=$false, HelpMessage="Tên dịch vụ trên Cloud Run")]
    [string]$ServiceName = "careconnect-api"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  TRIỂN KHAI CARECONNECT LÊN GOOGLE CLOUD RUN + NEON DB" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# Tự động sinh JWT Secret ngẫu nhiên an toàn nếu người dùng không nhập
if ([string]::IsNullOrWhiteSpace($JwtSecret)) {
    $bytes = New-Object Byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $JwtSecret = [Convert]::ToBase64String($bytes)
    Write-Host "[+] Đã tự động sinh khóa bảo mật JWT Secret chuẩn 256-bit." -ForegroundColor Green
}

Write-Host "[1/3] Thiết lập Google Cloud Project: $ProjectId ..." -ForegroundColor Yellow
gcloud config set project $ProjectId

Write-Host "[2/3] Bật các dịch vụ Cloud Run & Cloud Build API..." -ForegroundColor Yellow
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com

Write-Host "[3/3] Đang đóng gói Container & Deploy trực tiếp lên Cloud Run ($Region)..." -ForegroundColor Yellow
gcloud run deploy $ServiceName `
    --source . `
    --region $Region `
    --platform managed `
    --allow-unauthenticated `
    --port 8080 `
    --memory 512Mi `
    --cpu 1 `
    --min-instances 0 `
    --max-instances 3 `
    --set-env-vars "ASPNETCORE_ENVIRONMENT=Production,EnableSwagger=true,DATABASE_URL=$DatabaseUrl,Jwt__Key=$JwtSecret,GEMINI_API_KEY=$GeminiApiKey,Gemini__Model=gemini-2.5-flash"

Write-Host "============================================================" -ForegroundColor Green
Write-Host "  HOÀN TẤT TRIỂN KHAI CARECONNECT LÊN GOOGLE CLOUD RUN!" -ForegroundColor Green
Write-Host "  - Kiểm tra Health Check tại: https://<your-url>/health" -ForegroundColor Green
Write-Host "  - Tài liệu Scalar API tại : https://<your-url>/scalar/v1" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
