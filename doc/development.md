# 开发与贡献

本文档说明本地构建、测试和 GitHub Actions 发布流程。普通用户只需要下载 GitHub Release 压缩包，不需要安装 .NET SDK 或执行本地构建命令。

## 本地环境

主服务是 .NET Framework 4.8、x64 项目，编译时需要对应版本的 Siemens TIA Portal Openness API。离线测试是独立的 .NET 8 控制台项目，不依赖 TIA Portal。

开发机需要 Windows、.NET SDK 8、.NET Framework 4.8，以及 TIA Portal V20/V21 和 Openness 组件。运行 TIA 相关代码的 Windows 用户还需要属于 Siemens TIA Openness 用户组。

## 本地构建

构建 V21：

~~~powershell
dotnet restore .\src\TiaMcpServer\TiaMcpServer.csproj
dotnet build .\src\TiaMcpServer\TiaMcpServer.csproj -c Release
~~~

构建 V20：

~~~powershell
dotnet restore .\src\TiaMcpServer\TiaMcpServer.V20.csproj
dotnet build .\src\TiaMcpServer\TiaMcpServer.V20.csproj -c Release
~~~

如果找不到 Siemens 程序集，请确认 TIA 安装目录下存在 PublicAPI\V21\net48 或 PublicAPI\V20\net48，并使用项目支持的 TiaPortalLocation 配置。

## 测试

测试项目不是 VSTest 工程，必须使用 dotnet run：

~~~powershell
dotnet restore .\tests\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj
dotnet run --project .\tests\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj -c Release --no-restore
~~~

## GitHub Actions

.github/workflows/ci.yml 在 GitHub 托管的 Windows Runner 上执行离线测试和发布文件检查，触发于 main/master 的 Push、Pull Request 或手动运行。它不构建主服务，因为托管 Runner 没有 TIA Portal Openness。

.github/workflows/release.yml 在发布前先运行离线测试，然后使用 Windows Self-hosted Runner 构建 V21 主服务并打包 zip。推送 v* 标签时会自动创建 GitHub Release。

## 配置 Release Runner

1. 在仓库进入 Settings -> Actions -> Runners -> New self-hosted runner，选择 Windows x64。
2. 在安装了 TIA Portal V21、Openness、.NET Framework 4.8 和 .NET SDK 8 的机器上安装 Runner。
3. 给该 Runner 增加自定义标签 tia-v21。
4. 确保运行 Runner 的 Windows 用户属于 Siemens TIA Openness 用户组，并能访问 TIA 的 PublicAPI 目录。
5. 如果以后发布 V20，需要增加一个安装 TIA Portal V20 且带有 tia-v20 标签的 Runner，并扩展 Release 工作流。

公开仓库的 Pull Request 只使用 GitHub 托管 Runner。Self-hosted Runner 只执行受信任的 Release 工作流，不要让不受信任的 Pull Request 在其上运行。

## 发布流程

代码合并到主分支并确认 CI 通过后，创建版本标签：

~~~powershell
git tag v2.7.3
git push origin v2.7.3
~~~

Release 工作流完成后，GitHub Release 页面会出现 TiaMcpServer-v21-win-x64.zip。该压缩包由构建产物自动生成，包含 exe、托管依赖、README、skill/ 和用户组配置脚本。

## 用户组配置脚本

发布包内的 Configure-TiaOpenness.ps1 需要管理员 PowerShell。它会检查当前用户是否已经在 Siemens TIA Openness 组中，缺少时自动加入：

~~~powershell
.\Configure-TiaOpenness.ps1
~~~

只检查不修改：

~~~powershell
.\Configure-TiaOpenness.ps1 -CheckOnly
~~~

脚本执行成功后，用户需要注销并重新登录 Windows。脚本只配置 Windows 用户组，不安装 TIA Portal、Openness 或任何 Siemens 组件。

## 贡献检查

提交代码前至少运行离线测试。涉及 TIA Openness 的改动还要在对应版本的本机 TIA 环境中完成构建和验证。所有工程写入操作都应遵循 skill/SKILL.md 中的 Bootstrap、读取项目树、编译和保存顺序。
