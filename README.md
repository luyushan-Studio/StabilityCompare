# StabilityCompare

用于**曲线对比与验证**的 Windows 桌面工具，方便振动、噪音测试过程中的离线数据分析与调试。

从两个产品或测试批次的目录中，读取同名通道下的 `StabilityTime.csv`，叠加展示稳态波形，并通过差值曲线和统计指标检查信号差异。适合排查通道数据异常、比较不同测试条件，以及辅助验证采集与分析结果。

## 功能

- **双产品、同名通道配对**：仅加载 `StabilityTime.csv`，不混入升速或结束阶段数据。
- **大图分页展示**：默认“波形叠加”页，蓝色为 A、橙色为 B；“差值与统计”页显示 A−B 和统计结果。
- **局部对比**：缩放、拖动、十字光标、按采样点指定选区、将可见范围设为选区、放大到选区和复位缩放。
- **手动对齐**：按采样点设置 B 的偏移，正数向右移动；仅比较共同区间，不补零或拉伸数据。
- **统计验证**：均值、RMS、绝对峰值、峰峰值、RMSE、最大绝对差及相关系数。
- **重新加载**：从磁盘重新读取两份 CSV，替换旧数据；切换分页保留当前视图。
- **结果导出**：双图 PNG，以及包含文件路径、SHA-256、修改时间、选区和统计结果的 CSV 报告。

## 数据目录

```text
Data/
├─ Product_A/
│  ├─ Axis1/StabilityTime.csv
│  ├─ Axis2/StabilityTime.csv
│  └─ Axis3/StabilityTime.csv
└─ Product_B/
   ├─ Axis1/StabilityTime.csv
   ├─ Axis2/StabilityTime.csv
   └─ Axis3/StabilityTime.csv
```

产品目录和通道目录可自行命名；配对依据是两个产品下的**相同通道目录名**。界面列出双方均存在稳态文件的通道。

CSV 为 `Value` 表头的单列数值文件，例如：

```csv
Value
0.0123
-0.0456
0
1.23e-4
```

使用小数点 `.`，支持科学计数法和 UTF-8 BOM。合法零值保留；空数据、空行、非数值、NaN 或 Infinity 会报错，避免跳过采样后导致波形错位。

## 快速使用

1. 启动程序，浏览到包含产品子目录的数据根目录，点击“扫描目录”。
2. 选择产品 A、产品 B 及同名通道，点击“加载 / 重新加载”。
3. 在“波形叠加”页观察两条曲线，滚轮缩放、左键拖动。
4. 输入选区起点和终点后点击“应用”，再点击“放大到选区”查看局部波形。索引从 **0** 开始，终点包含在内。
5. 需要手动对齐时调整“B 偏移点数”，再点击“应用”。
6. 切换到“差值与统计”页查看指标或导出结果。

重新加载会重置选区和偏移。程序默认浏览“文档”目录；也可通过环境变量 `STABILITYCOMPARE_DATA_ROOT` 指定默认数据根目录。现场数据无需放入本仓库。

## 采样率与结果解释

- CSV 不含时间列，默认横轴为采样点序号。确认 A/B 采用**相同采样率**后，才勾选按秒显示并填入实际采样率；界面的 `32000` 是输入初值，不是对历史文件的判断。
- 纵轴表示 CSV 中保存的原始数值，不自动归一化、滤波或做传感器灵敏度换算。比较前应确认通道物理含义、单位和采集设置一致。
- 所有统计基于共同选区的完整采样。相关系数在常量序列下显示“不适用”。
- 本工具用于离线曲线验证与测试调试，当前不包含不同采样率重采样、自动对齐、FFT 频谱或生产合格判定。不能仅凭原始 CSV 保证复现生产算法的检测结论。

## 开发与构建

技术栈：**C# / .NET 8 / WinForms / ScottPlot.WinForms 5.0.39**。

运行环境为 Windows 和 .NET 8 Desktop Runtime；开发需要 .NET 8 或更高版本 SDK，以及支持 .NET 8 的 Visual Studio。使用 Visual Studio 时打开 `StabilityCompare.sln`，将 `StabilityCompare` 设为启动项目。

```powershell
dotnet restore StabilityCompare.sln
dotnet build StabilityCompare.sln -c Release
dotnet run --project StabilityCompare
```

发布为依赖运行时的应用：

```powershell
dotnet publish StabilityCompare/StabilityCompare.csproj -c Release -o publish
```

运行 `publish/StabilityCompare.exe`。分发时复制整个 `publish` 目录，不能只复制 EXE。中文图表使用 Windows 的微软雅黑字体文件。

## 测试

测试程序无需额外测试框架，覆盖相同曲线、正负偏移、长度不同、选区边界、常量序列、零值、错误 CSV、取消读取及重新读取。

```powershell
# 合成数据回归
dotnet run --project StabilityCompare.Tests

# 现场数据回归（当前固定校验 21 份、每份 320000 点的稳态样本）
dotnet run --project StabilityCompare.Tests -- "D:\TestData"
```

初版已通过上述现场样本的 38 项检查，并验证真实波形加载和分页显示。现场样本不随仓库发布；其他规模的数据集不适用当前固定数量的现场回归断言。PNG/CSV 导出功能尚未完成端到端人工验证。

ScottPlot 5.0.39 的旧版 OpenTK 传递依赖可能产生 `NU1701` 兼容性提示；本工具使用普通 `FormsPlot`，已在开发机验证显示。NuGet 网络不可用时也可能出现 `NU1900`，应在可联网环境完成依赖审计。

## 目录结构

```text
StabilityCompare.sln
StabilityCompare/
  Program.cs             # 程序入口及中文图表字体
  MainForm.cs            # 目录选择、分页绘图、交互及导出
  CompareData.cs         # CSV 读取、快照与比较统计
StabilityCompare.Tests/  # 离线回归检查
```

应用独立于生产采集工程，不引用板卡采集、PLC 或数据库业务模块。

## 第三方组件

绘图使用 [ScottPlot](https://github.com/ScottPlot/ScottPlot)，采用 MIT 许可；其他依赖保留各自许可。详见 [第三方组件说明](THIRD_PARTY_NOTICES.md)。
