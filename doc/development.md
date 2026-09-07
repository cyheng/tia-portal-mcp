# 开发与贡献

本文档说明本地构建、测试和 GitHub Actions 发布流程。普通用户从 GitHub Release 下载压缩包，按 README 中的环境要求解压运行。

## 本地环境

主服务面向 TIA Portal V21，使用 .NET Framework 4.8 和 x64，编译时引用本机 V21 的 Openness API。离线测试是独立的 .NET 8 控制台项目。

开发机需要 Windows x64、.NET SDK 8、.NET Framework 4.8 Developer Pack，以及 TIA Portal V21 和 Openness 组件。运行 TIA 相关代码的 Windows 用户还需要属于 Siemens TIA Openness 用户组。

## 本地构建

还原依赖并构建主服务：

~~~powershell
dotnet restore .\src\TiaMcpServer\TiaMcpServer.csproj
dotnet build .\src\TiaMcpServer\TiaMcpServer.csproj -c Release --no-restore
~~~

项目通过 Openness V21 构建包定位本机 `PublicAPI\V21\net48`。自定义安装目录可通过 MSBuild 的 `TiaPortalLocation` 属性指定：

~~~powershell
dotnet build .\src\TiaMcpServer\TiaMcpServer.csproj -c Release --no-restore -p:TiaPortalLocation='D:\TIA21\Portal V21'
~~~

Release 产物位于 `src/TiaMcpServer/bin/Release/net48`。运行时也使用 V21 安装目录，支持 `--tia-portal-location` 参数和 `TiaPortalLocation` 环境变量。

## 测试

测试项目以控制台程序执行，使用 `dotnet run` 运行检查：

~~~powershell
dotnet restore .\tests\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj
dotnet run --project .\tests\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj -c Release --no-restore
~~~

## GitHub Actions

.github/workflows/ci.yml 在 GitHub 托管的 Windows Runner 上执行离线测试和发布文件检查，触发于 main/master 的 Push、Pull Request 或手动运行。主服务构建由安装了 TIA Portal V21 Openness 的 Self-hosted Runner 执行。

.github/workflows/release.yml 在发布前先运行离线测试，然后使用 Windows Self-hosted Runner 构建 V21 主服务并打包 zip。推送 v* 标签时会自动创建 GitHub Release。

## 配置 Release Runner

1. 在仓库进入 Settings -> Actions -> Runners -> New self-hosted runner，选择 Windows x64。
2. 在安装了 TIA Portal V21、Openness、.NET Framework 4.8 和 .NET SDK 8 的机器上安装 Runner。
3. 给该 Runner 增加自定义标签 tia-v21。
4. 确保运行 Runner 的 Windows 用户属于 Siemens TIA Openness 用户组，并能访问 V21 安装目录中的 `PublicAPI\V21\net48`。

公开仓库的 Pull Request 使用 GitHub 托管 Runner。Self-hosted Runner 专用于受信任的 Release 工作流。

## 发布流程

代码合并到主分支并确认 CI 通过后，创建版本标签：

~~~powershell
git tag v2.7.3
git push origin v2.7.3
~~~

Release 工作流调用 `scripts/Package-TiaMcpRelease.ps1`，从 `src/TiaMcpServer/bin/Release/net48` 生成 `TiaMcpServer-v21-win-x64.zip` 并上传到 GitHub Release。压缩包包含 exe、托管依赖、README、`skill/`、`doc/` 和用户组配置脚本。

## 用户组配置脚本

发布包内的 Configure-TiaOpenness.ps1 需要管理员 PowerShell。它会检查当前用户是否已经在 Siemens TIA Openness 组中，缺少时自动加入：

~~~powershell
.\Configure-TiaOpenness.ps1
~~~

只读检查：

~~~powershell
.\Configure-TiaOpenness.ps1 -CheckOnly
~~~

脚本负责配置 Windows 用户组。执行成功后，用户需要注销并重新登录 Windows；TIA Portal V21 和 Openness 组件通过 Siemens 安装程序配置。

## 贡献检查

提交代码前至少运行离线测试。涉及 TIA Openness 的改动还要在本机 TIA Portal V21 环境中完成构建和验证。所有工程写入操作都应遵循 skill/SKILL.md 中的 Bootstrap、读取项目树、编译和保存顺序。
