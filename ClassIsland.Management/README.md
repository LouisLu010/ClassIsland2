# ClassIsland 集控

独立的集控服务器与管理员工作台。网页端、Android 和 iOS 共用 **Avalonia 12.1.1 + FluentAvalonia 3.0.0** 界面及 ViewModel；服务端使用 ASP.NET Core 10、SQLite，与现有 ClassIsland 的 `Cyrene_MSP 2.0.0.0` 协议兼容。

## 功能

- 校园概览：已登记、在线、待审批设备和排队任务数量，最近操作记录。
- 设备管理：加入申请、管理员审批、在线状态、名称与分组调整、停用、搜索与筛选。
- 班级分组：创建、编辑、删除空分组，生成加入配置、导出文件、轮换加入码。
- 课表与配置：导入完整 ClassIsland 档案（课表、时间表、科目）、应用设置、组件；导出配置，保存历史版本，按分组发布。
- 限制策略：9 项可视化策略开关。保存与发布分离，已发布内容固定到具体版本，防止编辑过程影响设备。
- 通知广播：指定设备或整个分组、紧急置顶、语音朗读、提示音、显示时长、重复次数。
- 远程任务：刷新配置、重启应用、配置回收；立即或定时发送，离线排队、24 小时过期、取消待下发任务。
- 配置回收：应用设置、档案、组件、自动化、日志、插件列表，查看与导出返回结果。
- 操作记录：管理操作及设备上报事件，搜索与 JSON 导出。
- 账户权限：管理员、操作员、只读成员；创建成员、停用账户、修改密码并撤销已有会话。
- 安全基础：PBKDF2 密码散列、数据库仅保存会话令牌散列、登录限流、设备审批、一次性回收请求、资源编辑并发检查、原子事务和审计。

## 项目结构

| 目录 | 用途 |
| --- | --- |
| `Server` | HTTP 管理 API、原有客户端 HTTP 配置接口、gRPC 设备服务、SQLite |
| `Contracts` | 管理 API DTO 与适配浏览器/iOS 的 JSON 源生成上下文 |
| `Client` | 三端共享的 Avalonia 界面、API 客户端及 MVVM |
| `Browser` | WebAssembly 网页入口 |
| `Android` | 独立管理员 Android 应用 |
| `iOS` | 独立管理员 iPhone/iPad 应用 |
| `Desktop` | 本地开发和验证宿主 |
| `Tests` | 服务逻辑、HTTP/gRPC 集成、ViewModel 和界面测试 |

## 本地启动

在仓库根目录执行。使用 .NET 10 SDK。第一次启动前，通过环境变量设置至少 12 个字符的管理员密码；不要将密码写入仓库：

```sh
read -s 'management_password?初始管理员密码：'
Management__BootstrapPassword="$management_password" dotnet run --project ClassIsland.Management/Server
```

以上交互输入命令适用于 macOS 的 zsh。其他 shell 可通过自己的环境变量配置方式设置 `Management__BootstrapPassword`。默认用户名为 `admin`，可用 `Management__BootstrapUser` 覆盖。数据库已有账户时不会重新设置密码；初始化后不再需要此环境变量。

默认监听：

- `http://localhost:5070`：网页、管理 API、设备配置下载。
- `http://localhost:5071`：设备 gRPC，使用 HTTP/2。

开发桌面工作台：

```sh
dotnet run --project ClassIsland.Management/Desktop
```

登录地址填写 `http://localhost:5070`。除 loopback 本地调试外，管理员客户端要求 HTTPS。

## 构建网页端

Avalonia Browser 需要链接 Skia 和 HarfBuzz，必须安装 `wasm-tools`，不能通过关闭 `WasmBuildNative` 来替代。

```sh
dotnet workload install wasm-tools
sh ClassIsland.Management/build-web.sh
dotnet run --project ClassIsland.Management/Server
```

脚本构建网页应用并将 WebAssembly 输出与网页入口复制到服务端 `wwwroot`。打开 `http://localhost:5070`。可设置 `DOTNET_COMMAND` 使用独立 SDK。网页与 API 使用同源部署，不需要开放跨域访问。

## Android 和 iOS

```sh
dotnet workload install android
dotnet build ClassIsland.Management/Android/ClassIsland.Management.Android.csproj -c Debug

dotnet workload install ios
dotnet build ClassIsland.Management/iOS/ClassIsland.Management.iOS.csproj -c Debug \
  -p:RuntimeIdentifier=iossimulator-arm64 -p:EnableCodeSigning=false
```

Android 还需要匹配的 Android SDK/JDK。iOS 需要与 .NET iOS 工作负载匹配的 Xcode；Intel Mac 使用 `iossimulator-x64`。真机使用 `ios-arm64` 并配置开发签名。管理员应用不引用课表播放器及 SoundFlow，因此没有主应用的仅真机音频库限制。发布到应用商店前仍需配置正式图标、开发者团队、签名及隐私申报。

## 连接班级设备

1. 在“设置与账户”填写设备可访问的网页/配置地址与设备连接地址。
2. 在“班级分组”创建班级，生成并导出加入配置。
3. 班级设备打开 ClassIsland 的“设置 → 集控 → 加入管理”，导入该文件。
4. 管理员在“设备管理”批准申请。设备将在下次重连时建立会话，旧客户端通常约 30 秒后重试。
5. 导入 ClassIsland 导出的完整档案或创建策略，保存后发布到分组。
6. 在“通知与任务”发送任务，并在记录中查看结果。

设备在注册期间只能读取组织名称。审批后才能下载分组配置。轮换加入码不会使已建立的设备会话失效，停用设备会停止其访问并取消待下发任务。

## 部署与数据

服务端默认只监听本机。正式使用时通过支持 HTTP/2 gRPC 的反向代理配置受信任 HTTPS 证书，同时设置 `Management` 页面中的公开地址。反向代理需要允许 gRPC 长连接，并将设备接口限制在校园网或可信 VPN 内。不要把开发用的明文端口直接暴露到公网。

`Management:DataDirectory`（环境变量 `Management__DataDirectory`）控制数据库及 PGP 密钥位置，默认为服务端工作目录下的 `data`。备份时停止服务并复制整个数据目录，恢复时也恢复原有服务器密钥；密钥变化会导致现有设备不再信任服务器。数据目录、生成的网页文件和构建产物均不提交到 Git。

当前使用单实例 SQLite，按集合存储 JSON，适合校内中小规模试用。列表只返回最近 500 条任务/审计，但数据库仍保存完整历史；尚未实现分页索引、自动归档、多实例共享连接状态或多租户隔离。规模扩大前需测量数据量与连接数并优化存储查询。

## 旧协议的边界

- 现有设备协议使用 CUID/MAC 与服务器 PGP 验证，**不提供设备私钥身份认证**；HTTP 档案下载也没有设备令牌。这些接口保持兼容，必须通过校园网/VPN 和反向代理访问控制限定来源。
- 因此不提供通过旧下载接口分发管理员密码或凭据的功能；上传应用设置前也应确认不含秘密。
- 旧客户端的配置上传不携带会话头，本服务只接受服务端主动创建、已下发、未过期且未完成的随机请求 ID，返回结果只供已登录管理成员读取。
- 旧协议没有通用执行回执。“已下发”不等于设备执行成功；只有配置回收收到结果时显示“已完成”。连接中断后的不确定下发不会自动重放，避免重复重启或广播。
- 当前主应用的 `DataUpdated` 只重新加载清单和策略。课表、组件在对应加载流程或下次重启时应用；必要时由管理员显式创建重启任务。默认应用设置仅用于尚未完成欢迎初始化的设备，不会远程覆写既有设备的全部设置。
- 系统级远程桌面、文件执行、设备关机、APNs/FCM 管理端推送和跨组织多租户尚未实现。

## 验证

```sh
dotnet build ClassIsland.Management/Server/ClassIsland.Management.Server.csproj -c Debug
dotnet build ClassIsland.Management/Desktop/ClassIsland.Management.Desktop.csproj -c Debug
dotnet test ClassIsland.Management/Tests/ClassIsland.Management.Tests.csproj --collect:"XPlat Code Coverage"
dotnet test ClassIsland.Management/UITests/ClassIsland.Management.UITests.csproj
```

平台应用的完整构建需要上文对应的工具链；通过共享 UI 编译或单元测试不代表已完成移动真机验证。

## GitHub Actions

[`Management CI`](../.github/workflows/management_ci.yml) 在 `dev/v2/management`、`master` 的相关改动 push、相关 PR 和手动触发时运行。各任务只使用只读仓库权限，不需要配置发布证书或仓库 Secret。

| 任务 | 检查与产物 |
| --- | --- |
| Server, Web and tests | 业务/API 测试、服务端行覆盖率至少 80%、共享界面测试、桌面宿主编译、网页与 Linux x64 服务端包 |
| Android debug APK | .NET 10、JDK 21、Android SDK 36，生成带调试签名的 arm64 APK |
| iOS Simulator | 固定 .NET SDK/工作负载 10.0.202 和 Xcode 26.3，生成 arm64 模拟器 `.app` 压缩包 |

Actions 页面保留构建产物和 TRX/Cobertura 测试报告 14 天。服务端包包含网页文件，运行需要 .NET 10 ASP.NET Core Runtime；启动前仍需配置初始管理员密码。Android APK 使用调试签名，iOS 产物仅用于 Apple Silicon 模拟器，不是可在真机安装的 IPA。工作流不会创建 GitHub Release 或部署服务器。
