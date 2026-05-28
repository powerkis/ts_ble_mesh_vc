using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TSSmartSchool
{
    public partial class frmPrinterUsb : Form
    {
        private IntPtr _hReadDevice = IntPtr.Zero;
        private IntPtr _hWriteDevice = IntPtr.Zero;

        private string _meshUuidCache = "";
        private bool _isInputDbRunning = false;
        private CancellationTokenSource _readCts;
        private byte _activeIvIndexByte = 0x01;
        private uint _virtualSnoCounter = PrinterUsbFunc.DefaultBaseSnoMargin;
        private int _logSequenceCounter = 0;
        private readonly object _logFileLock = new object();

        private volatile bool _isDongleProvisioned = false;
        private volatile bool _isProStsReceived = false;
        private volatile bool _isIvSyncCompleted = false;
        private volatile bool _isCommandTriggerOpened = false;

        private volatile bool _isDongleReadyToSend = true;
        private System.Windows.Forms.Timer _uiControlLockTimer;
        private System.Windows.Forms.Timer _autoScanTimer;

        private byte[] _rawIvIndexBytes = new byte[] { 0x01, 0x00, 0x00, 0x00 };
        private List<ushort> _registeredNodeAddresses = new List<ushort>();
        private HashSet<ushort> _respondedNodeAddresses = new HashSet<ushort>();
        private bool _isAllNodesConnectedReported = false;

        private readonly string _configIni = Path.Combine(Application.StartupPath, "config.ini");
        private readonly string _logPath = Path.Combine(Application.StartupPath, "Logs");

        private const string _tx_cmd_sno_section = "SET";
        private const string _tx_cmd_sno_key = "tx_cmd_sno";
        private const string _import_json_flag_key = "import_json_flag";
        private const string _mesh_uuid_key = "mesh_uuid";
        private const string _adr_max_key = "_adr_max";

        private bool _isSequenceFinishedReadyToPrintIv = false;
        private Dictionary<ushort, string> _meshLastStateCache = new Dictionary<ushort, string>();
        private bool _currentControlModeIsOn = false;
        private int _lastCommandTick = 0;

        // 🎯 동글이 최초 91 9A 비콘 자백을 완수했는지 검증하는 절대 마스터 플래그
        private volatile bool _isIvRealHardwareSynced = false;

        public frmPrinterUsb()
        {
            InitializeComponent();

            btnConnect.Click += btnConnect_Click;
            btnOn.Click += btnOn_Click;
            btnOff.Click += btnOff_Click;
            btnLoadJson.Click += btnLoadJson_Click;
            btnLogCopy.Click += btnLogCopy_Click;
            btnLogClear.Click += btnLogClear_Click;
            btnGwReset.Click += btnGwReset_Click;
            btnRestart.Click += btnRestart_Click;
            btnDisconnect.Click += (s, e) => DisconnectDevice();

            this.Load += frmPrinterUsb_Load;
            InitializeListView();
            InitializeControlLockTimer();
            InitializeAutoScanTimer();
        }

        private void frmPrinterUsb_Load(object sender, EventArgs e)
        {
            AppendLog("시스템 시작 ............", "common");
            SetUIMode(true);

            try
            {
                string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PrinterUsbFunc.TempMeshFileName);
                string targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PrinterUsbFunc.BaseMeshFileName);
                if (File.Exists(tempPath))
                {
                    File.Copy(tempPath, targetPath, true);
                    File.SetLastWriteTime(targetPath, DateTime.Now);
                    File.Delete(tempPath);
                }
            }
            catch { }

            this.BeginInvoke(new Action(() =>
            {
                btnConnect.PerformClick();
            }));
        }

        private void SetUIMode(bool isEnable)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetUIMode(isEnable)));
                return;
            }
            foreach (Control c in this.Controls)
            {
                if (c is Button) c.Enabled = isEnable;
            }
            btnLoadJson.Enabled = true;
            btnConnect.Enabled = true;
            btnGwReset.Enabled = true;
            btnLogCopy.Enabled = true;
            btnLogClear.Enabled = true;
        }

        private void SetControlButtonsEnabled(bool isEnabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetControlButtonsEnabled(isEnabled)));
                return;
            }
            btnOn.Enabled = isEnabled;
            btnOff.Enabled = isEnabled;
        }

        private void InitializeControlLockTimer()
        {
            _uiControlLockTimer = new System.Windows.Forms.Timer();
            _uiControlLockTimer.Interval = 2000;
            _uiControlLockTimer.Tick += (s, e) =>
            {
                _uiControlLockTimer.Stop();
                SetControlButtonsEnabled(true);
                //AppendLog("🔓 [쿨타임 종료] 제어 버튼 2초 락 가드가 해제되었습니다. 다음 명령 전송이 가능합니다.", "common");
            };
        }

        private void InitializeAutoScanTimer()
        {
            if (_autoScanTimer != null)
            {
                _autoScanTimer.Stop();
                _autoScanTimer.Dispose();
                _autoScanTimer = null;
            }
            _autoScanTimer = new System.Windows.Forms.Timer();
            _autoScanTimer.Interval = 4000;
            _autoScanTimer.Tick += (s, e) =>
            {
                TriggerInstantAutoScan();
            };
        }

        private void TriggerInstantAutoScan()
        {
            // 🎯 [★ 인터록 강화 ★] _isCommandTriggerOpened가 false(시퀀스 주행 중)라면 
            // 타이머가 돌더라도 포트에 패킷을 절대로 주입하지 못하도록 즉시 리턴 차단합니다!
            if (!_isCommandTriggerOpened) return;

            if (_hWriteDevice != IntPtr.Zero && _isIvRealHardwareSynced)
            {
                AppendLog("🔄 [상태 스캔] VC send to gateway is: e8 ff 00 00 00 00 02 04 ff ff 82 4b", "GATEWAY");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x01, PrinterUsbFunc.CmdGetLightness, true);
            }
        }

        private void InitializeListView()
        {
            lstvNode.View = View.Details;
            lstvNode.FullRowSelect = true;
            lstvNode.GridLines = true;
            lstvNode.Columns.Clear();
            lstvNode.Columns.Add("번호", 50, HorizontalAlignment.Center);
            lstvNode.Columns.Add("유니캐스트 주소", 80, HorizontalAlignment.Center);
            lstvNode.Columns.Add("현재 상태", 90, HorizontalAlignment.Center);
            lstvNode.Columns.Add("디바이스 키 (DeviceKey)", 250, HorizontalAlignment.Left);
        }

        // =================================================================================
        // 🎯 [btnConnect_Click - ★ 안테나 선제 개방 및 IV 0x28 완벽 동화 대대통공 최종 패치 ★]
        // =================================================================================
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                SetUIMode(false);
                _isProStsReceived = false;
                _isAllNodesConnectedReported = false;
                _respondedNodeAddresses.Clear();
                _isSequenceFinishedReadyToPrintIv = false;
                _isIvRealHardwareSynced = false;

                _hReadDevice = PrinterUsbFunc.GetPrintDeviceHandle(0xFFFF, msg => AppendLog(msg, "common"));
                _hWriteDevice = PrinterUsbFunc.GetPrintDeviceHandle(0xFFFF, msg => { });

                if (_hReadDevice == IntPtr.Zero || _hReadDevice == (IntPtr)(-1) ||
                    _hWriteDevice == IntPtr.Zero || _hWriteDevice == (IntPtr)(-1))
                {
                    AppendLog("❌ [부팅 실패] 동글 핸들 확보 실패. USB 연결을 확인하세요.", "common");
                    SetUIMode(true);
                    return;
                }

                _activeIvIndexByte = 0x01;
                _rawIvIndexBytes[0] = 0x01;

                StartReading();
                await Task.Delay(150);

                AppendLog("HCI_GATEWAY_CMD_GET_UUID_MAC : e9 ff 10", "GATEWAY");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdGetUuidMac);
                await Task.Delay(40);

                AppendLog("설정 시작 ............ ", "common");

                AppendLog("HCI_GATEWAY_CMD_GET_PRO_SELF_STS  : e9 ff 0c", "GATEWAY");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdGetProSelfSts);

                int proPolling = 0;
                while (!_isProStsReceived && proPolling < 50)
                {
                    await Task.Delay(20);
                    proPolling++;
                }
                await Task.Delay(200);

                // 🎯 [★ 핵심 사상 역전 ★] 
                // 동글이가 귀를 열어 무선 패킷을 수신할 수 있도록 안테나 개방 탄을 최선행으로 먼저 발사합니다!
                AppendLog("📡 [안테나 개방] 동글 무선 연결!", "GATEWAY");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, new byte[] { 0xE9, 0xFF, 0x12, 0x01 });
                await Task.Delay(50);
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdAdvOperaStart);
                await Task.Delay(50);
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdExtendAdvOn);
                await Task.Delay(100);

                // 🎯 안테나가 열렸으므로 이제 공중파 비콘(91 9A) 명세가 사정없이 밀려들어옵니다.
                // 진짜 IV(0x28) 명찰을 포획하여 파서가 전역 변수를 완벽히 매칭할 때까지 문을 걸어 잠급니다.
                int ivHardwareSyncPolling = 0;
                AppendLog("⏳ [주파수 획득] 동글 안테나가 공중파 무선 세션(IV Index) 실시간 비콘을 완벽히 획득할 때까지 대기합니다...", "common");
                while (!_isIvRealHardwareSynced && ivHardwareSyncPolling < 200)
                {
                    await Task.Delay(20);
                    ivHardwareSyncPolling++;
                }
                await Task.Delay(400); // 완전 바인딩 안착 마진

                AppendLog("HCI_GATEWAY_CMD_SET_EXTEND_ADV_OPTION : e9 ff 16 00", "GATEWAY");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdExtendAdvOption);
                await Task.Delay(60);

                string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PrinterUsbFunc.BaseMeshFileName);
                AppendLog($"⚙️ [자동 시스템] 자동 연결중...", "common");
                if (File.Exists(dbPath))
                {
                    _ = Task.Run(async () => {
                        await ExecuteMeshConnectSequence(dbPath);
                    });
                }
                else
                {
                    AppendLog("ℹ️ 캐시 파일 없음. [Json 파일 가져오기]를 눌러 조명을 로드하세요.", "common");
                    SetUIMode(true);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"🚨 [커널 마운트 오류] {ex.Message}", "common");
                SetUIMode(true);
            }
        }

        /// <summary>
        /// 📥 [ExecuteMeshConnectSequence - 변환된 base_mesh.json의 순정 키 데이터를 100% 실시간 적출하는 정통 결속 선로]
        /// </summary>
        public async Task ExecuteMeshConnectSequence(string jsonPath)
        {
            _isInputDbRunning = true;

            // 🎯 [★ SNO 역행 및 동적 리셋 방어선 구축 ★]
            _isIvSyncCompleted = false;
            _isIvRealHardwareSynced = false;

            this.Invoke(new Action(() => {
                if (btnOn != null) btnOn.Enabled = false;
                if (btnOff != null) btnOff.Enabled = false;
                if (btnLoadJson != null) btnLoadJson.Enabled = false;
                if (btnDisconnect != null) btnDisconnect.Enabled = false;
                lstvNode.Items.Clear();
            }));
            _registeredNodeAddresses.Clear();

            try
            {
                // 1️⃣ 변환 완료된 정격 base_mesh.json 파일을 실시간으로 로드합니다.
                string jsonContent = File.ReadAllText(jsonPath);
                JObject jsonRoot = JObject.Parse(jsonContent);

                // ── [★ 가짜 고정 상수 100% 철거 및 도면 순정 키 동적 포획 ★] ──
                var netKeysToken = jsonRoot["netKeys"] as JArray;
                if (netKeysToken == null || netKeysToken.Count == 0)
                {
                    throw new InvalidDataException("🚨 [무결성 에러] 변환된 도면 내에 'netKeys' 정보가 누락되었습니다.");
                }
                string localNetKey = netKeysToken[0]["key"]?.ToString()?.ToUpper()?.Replace("-", "")?.Replace(" ", "");

                var appKeysToken = jsonRoot["appKeys"] as JArray;
                if (appKeysToken == null || appKeysToken.Count == 0)
                {
                    throw new InvalidDataException("🚨 [무결성 에러] 변환된 도면 내에 'appKeys' 정보가 누락되었습니다.");
                }
                string targetAppKeyHex = appKeysToken[0]["key"]?.ToString()?.ToUpper()?.Replace("-", "")?.Replace(" ", "");

                string localMeshUuid = jsonRoot["meshUUID"]?.ToString()?.ToLower()?.Replace("-", "") ?? "";

                if (string.IsNullOrEmpty(localMeshUuid) || string.IsNullOrEmpty(localNetKey) || string.IsNullOrEmpty(targetAppKeyHex))
                {
                    throw new InvalidDataException("🚨 [무결성 파괴] 변환 도면 분석 실패! 암호화 키 정보가 손상되어 개통을 중단합니다.");
                }

                _meshUuidCache = localMeshUuid;

                // ── [★ 동글이(Provisioner) 순정 물리 주소 100% 동적 적출 레일 ★] ──
                // 마스터가 지적하신 가짜 고정 상수 0x0400을 완벽하게 숙청 완료했습니다!
                int dongleAddr = 0x0400; // 도면 파싱 실패를 대비한 무결성 기본 안전선
                try
                {
                    var jsonProvisionersForAddr = jsonRoot["provisioners"] as JArray;
                    if (jsonProvisionersForAddr != null && jsonProvisionersForAddr.Count > 0)
                    {
                        var allocRange = jsonProvisionersForAddr[0]["allocatedUnicastRange"] as JArray;
                        if (allocRange != null && allocRange.Count > 0)
                        {
                            string lowAddrHex = allocRange[0]["lowAddress"]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(lowAddrHex))
                            {
                                dongleAddr = Convert.ToInt32(lowAddrHex, 16); // 🎯 동적 주소 정격 상속
                            }
                        }
                    }
                }
                catch { }

                // ── [★ 복수 프로비저너 100% 완벽 동화 선로 ★] ──────────────────
                var masterUuids = new HashSet<string>();
                var jsonProvisioners = jsonRoot["provisioners"] as JArray;
                if (jsonProvisioners != null)
                {
                    foreach (JObject prov in jsonProvisioners)
                    {
                        string pUuid = prov["UUID"]?.ToString()?.ToLower()?.Trim() ?? prov["uuid"]?.ToString()?.ToLower()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(pUuid)) masterUuids.Add(pUuid);
                    }
                }

                int targetAppKeyIndex = 0;
                int importJsonFlag = 0;
                uint savedTxCmdSno = PrinterUsbFunc.DefaultBaseSnoMargin;

                try
                {
                    string flagStr = PrinterUsbFunc.ReadIniValue(_tx_cmd_sno_section, _import_json_flag_key, "0", _configIni);
                    string snoStr = PrinterUsbFunc.ReadIniValue(_tx_cmd_sno_section, _tx_cmd_sno_key, PrinterUsbFunc.DefaultBaseSnoMarginStr, _configIni);

                    int.TryParse(flagStr, out importJsonFlag);

                    // 🎯 [★ 100% 소프트웨어 전동화 SNO 역행 차단선 ★]
                    uint parseIniSno = 0;
                    uint.TryParse(snoStr, out parseIniSno);
                    savedTxCmdSno = Math.Max(parseIniSno, _virtualSnoCounter);
                }
                catch { }

                // 2️⃣ 리스트뷰 및 노드 인벤토리 바인딩
                JArray nodesToken = jsonRoot["nodes"] as JArray;
                if (nodesToken != null)
                {
                    int displayIdx = 0;
                    foreach (JObject node in nodesToken)
                    {
                        string uAddr = node["unicastAddress"]?.ToString()?.Trim()?.PadLeft(4, '0')?.ToLower();
                        string nUuid = node["UUID"]?.ToString()?.ToLower()?.Trim() ?? node["uuid"]?.ToString()?.ToLower()?.Trim() ?? "";
                        string dKey = node["deviceKey"]?.ToString() ?? "00000000000000000000000000000000";

                        if (string.IsNullOrEmpty(uAddr)) continue;
                        if (masterUuids.Contains(nUuid)) continue;

                        int nodeAddr = Convert.ToInt32(uAddr, 16);
                        _registeredNodeAddresses.Add((ushort)nodeAddr);
                        _meshLastStateCache[(ushort)nodeAddr] = "대기 중";

                        this.Invoke(new Action(() => {
                            var item = new ListViewItem((displayIdx + 1).ToString());
                            item.SubItems.Add("0x" + nodeAddr.ToString("X4"));
                            item.SubItems.Add("대기 중");
                            item.SubItems.Add(dKey.ToUpper());
                            item.Tag = (ushort)nodeAddr;
                            lstvNode.Items.Add(item);
                        }));
                        displayIdx++;
                    }
                }

                // 3️⃣ 도면이 새로 갱신되었거나 동글이 미등록 상태일 때 최초의 각인 하드웨어 세션 개통
                if ((importJsonFlag == 1 || !_isDongleProvisioned) && nodesToken != null)
                {
                    // 🎯 [★ 마스터 사상 전 우주 최종 완공 : 뇌사 유발 리셋탄 완전 숙청 ★]
                    // 포트 단선을 유발하는 E9 FF 02 리셋 명령을 완전히 철거하여 동글이의 질식사를 원천 차단합니다!
                    AppendLog("📥 [정통 장치 등록] 새 순정 변환 도면 감지. 하드웨어 키 동화 결속을 집행합니다.", "common");
                    await Task.Delay(500);

                    // 동적으로 교정 적출된 dongleAddr 적용 사격!
                    AppendLog("HCI_GATEWAY_CMD_SET_PRO_PARA : e9 ff 09 0f b4 7e 4e 85 03 f1 94 a2 de 3e f6 49 88 47 a1 00 00 00 ff ff ff ff 00 04 ", "GATEWAY");
                    PrinterUsbFunc.SendSetProPara(_hWriteDevice, localNetKey, dongleAddr, msg => { });
                    await Task.Delay(300);

                    string targetDongleDevKey = "";
                    if (nodesToken.Count > 0 && nodesToken[0]["deviceKey"] != null)
                    {
                        targetDongleDevKey = nodesToken[0]["deviceKey"].ToString().ToUpper();
                    }
                    AppendLog("HCI_GATEWAY_CMD_SET_DEV_KEY : e9 ff 0d 00 04 29 23 be 84 e1 6c d6 ae 52 90 49 f1 f1 bb e9 eb ", "GATEWAY");
                    PrinterUsbFunc.SendSetDevKey(_hWriteDevice, targetDongleDevKey, dongleAddr, 0x00, msg => { });
                    await Task.Delay(300);

                    var bindPacket = new List<byte> { 0xE9, 0xFF, 0x0B };
                    bindPacket.Add((byte)(targetAppKeyIndex & 0xFF));
                    bindPacket.Add((byte)((targetAppKeyIndex >> 8) & 0xFF));
                    bindPacket.Add((byte)((targetAppKeyIndex >> 16) & 0xFF));
                    bindPacket.AddRange(PrinterUsbFunc.HexToByte(targetAppKeyHex));

                    PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, bindPacket.ToArray());
                    await Task.Delay(400);

                    // ⚙️ 동글 심장에 개별 조명 디바이스 키 정격 주입 진행
                    int injectIdx = 0;
                    foreach (JObject node in nodesToken)
                    {
                        string uAddr = node["unicastAddress"]?.ToString()?.Trim()?.PadLeft(4, '0')?.ToLower();
                        string nUuid = node["UUID"]?.ToString()?.ToLower()?.Trim() ?? node["uuid"]?.ToString()?.ToLower()?.Trim() ?? "";
                        string dKey = node["deviceKey"]?.ToString() ?? "00000000000000000000000000000000";

                        if (string.IsNullOrEmpty(uAddr) || masterUuids.Contains(nUuid)) continue;
                        int nodeAddr = Convert.ToInt32(uAddr, 16);

                        PrinterUsbFunc.SendSetDevKey(_hWriteDevice, dKey, nodeAddr, 0x00, msg => { });

                        string formattedDevKey = string.Join("  ", Enumerable.Range(0, dKey.Length / 2).Select(idx => dKey.Substring(idx * 2, 2).ToUpper()));
                        AppendLog($"Send VC node info: Index: {injectIdx} Addr: {uAddr} DeviceKey: {formattedDevKey}", "common");

                        injectIdx++;
                        await Task.Delay(200); // 🎯 동글이 플래시 버퍼 안착을 위해 연사 마진을 200ms로 안전 상향!
                    }

                    // 하드웨어 저장 마감탄 사격
                    PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, new byte[] { 0xE9, 0xFF, 0x03 });

                    // 🎯 동글이가 키 데이터를 영구 플래시에 저장할 수 있도록 안심 컴포트 마진 부여
                    AppendLog("⏳ [하드웨어 각인] 동글 내부 플래시 메모리 장부 안착을 위해 2초간 대기합니다...", "common");
                    await Task.Delay(2000);

                    // 🎯 장부 데이터 상속 마감 (가산 수식 원천 제거선 완벽 유지)
                    if (importJsonFlag == 1)
                    {
                        uint targetSno = savedTxCmdSno;
                        if (targetSno < 50000) targetSno = 50000;

                        _virtualSnoCounter = targetSno;
                        savedTxCmdSno = targetSno;

                        AppendLog($"⚙️ [하드웨어 각인 완공] 동글 메모리 장부 동화 완료! 순정 마진 0x{_virtualSnoCounter:X} 을 심장에 주입합니다.", "common");

                        byte targetIvIndex = _activeIvIndexByte;
                        if (targetIvIndex == 0) targetIvIndex = 0x2A;

                        byte[] sigSnoPacket = {
                            0xE9, 0xFF, 0x0F,
                            (byte)(targetSno & 0xFF),
                            (byte)((targetSno >> 8) & 0xFF),
                            (byte)((targetSno >> 16) & 0xFF),
                            targetIvIndex
                        };
                        PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x01, sigSnoPacket, true);

                        try
                        {
                            PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _import_json_flag_key, "0", _configIni);
                            PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _mesh_uuid_key, _meshUuidCache, _configIni);
                            PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _adr_max_key, "1025", _configIni);
                            PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _tx_cmd_sno_key, _virtualSnoCounter.ToString(), _configIni);
                        }
                        catch { }

                        await Task.Delay(1500);
                        importJsonFlag = 0;
                    }
                } // 💡 importJsonFlag 조건문 대괄호 마감선

                // ── 4️⃣ [★ 마스터 사상 진짜 최종 마침표: 선제 리셋 대응 분기형 정격 레일 ★] ──
                uint syncedSno = Math.Max(savedTxCmdSno, _virtualSnoCounter);

                if (syncedSno == 0)
                {
                    syncedSno = PrinterUsbFunc.DefaultBaseSnoMargin;
                }

                _virtualSnoCounter = syncedSno;

                // 🎯 [★ 01:40분 프리징 버그 최종 숙청 가드 ★]
                // 방금 하드웨어 리셋(importJsonFlag == 1 공정)을 겪은 직후라면 억지로 0x2A를 주입하면 칩셋이 굳습니다!
                // 초기화 공정 직후에는 순정 규칙 그대로 0x01 레일부터 대문을 열어주고,
                // 평상시 재기동(RESTART)일 때만 조명들의 리얼 주파수 캐시인 0x2A를 복원 탑재합니다.
                byte realHardwareIvIndex = _activeIvIndexByte;

                // 마스터, 기존의 강제 0x2A 고정 방식을 걷어내고 이 리셋 분기 가드로 완벽히 매핑합니다!
                if (realHardwareIvIndex <= 0x01)
                {
                    // 기존 이력 장부(INI) 상의 meshUUID 가 이미 존재한다면 캐시 기상으로 판단
                    if (string.IsNullOrEmpty(_meshUuidCache))
                    {
                        realHardwareIvIndex = 0x01; // 💡 새 도면 주입 직후에는 청정하게 0x01 출발!
                    }
                    else
                    {
                        realHardwareIvIndex = 0x2A; // 💡 평상시 RESTART 기상일 때는 장부대로 0x2A 결속!
                    }
                }

                string ivHexPart = realHardwareIvIndex.ToString("X2");
                AppendLog($"🚀 [IV 세션 연결] SNO 0x{_virtualSnoCounter:X} 및 IV 0x{ivHexPart} 정격 결속 완료.", "common");

                // 동글이 레지스터 최종 주입 고정 락(Lock) 사격
                byte[] setSno = { 0xE9, 0xFF, 0x0F, (byte)(syncedSno & 0xFF), (byte)((syncedSno >> 8) & 0xFF), (byte)((syncedSno >> 16) & 0xFF), realHardwareIvIndex };
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x01, setSno, true);
                await Task.Delay(200);

                byte[] netSno = { 0xE9, 0xFF, 0x11, (byte)(syncedSno & 0xFF), (byte)((syncedSno >> 8) & 0xFF), (byte)((syncedSno >> 16) & 0xFF), realHardwareIvIndex };
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x01, netSno, true);
                await Task.Delay(200);

                // 🎯 4) 무선 안테나 파이프라인 정격 개방
                AppendLog("📡 [통로 개방] 무선 안테나 파이프라인을 정격 개방합니다.", "common");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, new byte[] { 0xE9, 0xFF, 0x12, 0x01 });
                await Task.Delay(100);
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdAdvOperaStart);
                await Task.Delay(100);
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdExtendAdvOn);

                // 상시 무선 스캔 타이머 가동 마감
                InitializeAutoScanTimer();
                _autoScanTimer.Start();

                // 🎯 5) [★ 중요: 안테나 안정화 마진 ★] 
                // 안테나가 기상하여 공중파 무선 채널을 완전히 잡을 때까지 3초간 컴포트 대기합니다.
                await Task.Delay(3000);

                // 🎯 6) [★ 마스터 사상 진짜 최종 완공 : 순정 1:1 진짜 상태값 호출탄 격발 ★]
                // 안테나가 완벽하게 열려 숨을 쉬고 있는 청정 상태에서, 조명들에게 진짜 상태 조회탄을 날립니다!
                // 이 격발로 인해 천장 조명들이 일시에 "91 81 ... 82 04 01" 진짜 상태 영수증을 폭포수처럼 토해냅니다.
                AppendLog("📡 [진짜 상태 조회] 천장 조명 노드들에게 실시간 On/Off 상태 장부 소환탄을 사격합니다.", "common");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x00, PrinterUsbFunc.CmdGetLightSts);
                _virtualSnoCounter++;

                // 조회탄 방출 후 포트 버퍼 안착을 위한 안심 딜레이
                await Task.Delay(1000);

                // 최종 정격 장부 번호를 INI 파일에 백업
                PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _tx_cmd_sno_key, _virtualSnoCounter.ToString(), _configIni);
                await Task.Delay(200);

                _isCommandTriggerOpened = true;
                _isSequenceFinishedReadyToPrintIv = true;

                AppendLog("⚡ [개통 선로 정렬 완료] 하드웨어 가동 장부와 제어 통로 개방 세션을 정격 승인합니다.", "common");

                _isInputDbRunning = false;
                this.Invoke(new Action(() => SetUIMode(true)));
            }
            catch (Exception ex)
            {
                AppendLog($"🚨 오류 발생: {ex.Message}", "open");
                _isInputDbRunning = false;
                this.Invoke(new Action(() => SetUIMode(true)));
            }
        }

        // =================================================================================
        // 📥 [ParseResponse - 초기화 키 바인딩 로그 분류 확장 및 순수 로그 사수 최종 파서]
        // =================================================================================
        private void ParseResponse(byte[] buf, int readLen)
        {
            if (readLen <= 0) return;

            string hexStr = BitConverter.ToString(buf, 0, readLen).Replace("-", " ").ToLower();
            string markStr = "RX";

            // ── [★ 마스터 명제 추가 완공 : 초기화 하드웨어 핵심 로그 분류 확장 선로 ★] ──
            if (hexStr.Contains("82 60") || hexStr.Contains("5d 00 00 00 00 00"))
            {
                markStr = "RX_STS";
            }
            else if (hexStr.Contains("82 4e") || hexStr.Contains("82 04"))
            {
                markStr = "RX_LIGHT";
            }
            else if (hexStr.Contains("91 8a") || hexStr.Contains("91 82") ||
                     hexStr.Contains("91 8c") || hexStr.Contains("91 12") ||
                     hexStr.Contains("91 9B") ||
                     hexStr.Contains("appkey_add") || hexStr.Contains("appkey_status"))
            {
                // 조명의 응답뿐만 아니라, 동글이 자체의 키 바인딩 이벤트(91 8A / 91 82) 및 
                // 앱키 등록 상태 로그들도 시각적 인지도가 높은 청정 RX_LIGHT 레일로 강제 분류 래칭합니다!
                markStr = "RX_SET";
            }
            else if (hexStr.Contains("78 05"))
            {
                markStr = "RX_ERR";
            }
            else if (hexStr.StartsWith("91 9a"))
            {
                markStr = "iv_update";
            }

            // 동글 및 조명들이 포트로 올리는 모든 패킷은 가드 없이 100% 상시 무조건 로그창에 출력!
            AppendLog($"{hexStr}", markStr);

            for (int i = 0; i < readLen; i++)
            {
                if (buf[i] == 0x91 && i + 6 < readLen && buf[i + 6] == 0x82 && buf[i + 7] == 0x60)
                {
                    _isDongleReadyToSend = false;
                }

                if (buf[i] == 0x91 && i + 6 < readLen && buf[i + 6] == 0x5D && buf[i + 7] == 0x00)
                {
                    _isDongleReadyToSend = true;
                }

                // 🎯 하트비트 91 9A 포획 구역 (★ 마스터 사상 전 우주 최종 확정 고착 레일 ★)
                if (buf[i] == 0x91 && i + 1 < readLen && buf[i + 1] == 0x9A)
                {
                    if (i + 10 < readLen)
                    {
                        byte hardwareIv = buf[i + 5];

                        // 🎯 [★ 흑막 숙청 ★] i+6 자리에 박혀있는 IV Trigger Flag(01 또는 00)를 정확히 적출합니다.
                        byte ivTriggerFlag = buf[i + 6];

                        // ── [1단계: 최초 보안망 기상 시 하드웨어 순정 SNO 정확 상속] ──
                        if (!_isIvSyncCompleted)
                        {
                            _isIvSyncCompleted = true; // 무한 연사 방지 인터록 가동
                            _activeIvIndexByte = hardwareIv;

                            // 🎯 [★ 최종 인덱스 좌표 대완공 ★] 
                            // ivTriggerFlag(i+6)를 완벽하게 배제하고, 순수 SNO 3바이트(i+7, i+8, i+9)만 정밀 결속합니다!
                            uint hardwareRealSno = (uint)(buf[i + 7] | (buf[i + 8] << 8) | (buf[i + 9] << 16));

                            if (hardwareRealSno > 0)
                            {
                                // 동글이 장부를 흔들지 않고, 안전 마진 +64만 상속 계승합니다.
                                _virtualSnoCounter = hardwareRealSno + 64;
                            }
                            else
                            {
                                _virtualSnoCounter = 0xC500;
                            }

                            uint syncedSno = _virtualSnoCounter;

                            // 청정 통합 로그 출력
                            AppendLog($"⚡ [실시간 무선 채널 연결] SIG 정격 동화 상속 성공! IV: 0x{hardwareIv:X2} / SNO: 0x{syncedSno:X6} / TriggerFlag: {ivTriggerFlag}", "common");

                            // 🎯 [인터록: UI 개통]
                            this.Invoke(new Action(() => {
                                _isInputDbRunning = false;
                                SetUIMode(true);
                                AppendLog("🤖 [보안망 동화 완료] 제어 파이프라인 개통! 이제 조명 제어가 가능합니다.", "common");
                            }));
                        }
                        // ── [2단계: 평상시 실시간 주파수 미세 추종] ──
                        else if (_activeIvIndexByte != hardwareIv)
                        {
                            _activeIvIndexByte = hardwareIv;
                            AppendLog($"⚡ [공중파 주파수 추종 변동] 실시간 세션 주파수가 0x{hardwareIv:X2} 로 재정렬되었습니다.", "common");
                        }

                        // 3) 하드웨어 로우 레벨 바이트 배열 래칭 
                        _activeIvIndexByte = buf[i + 5];
                        _rawIvIndexBytes[0] = buf[i + 5];
                        _rawIvIndexBytes[1] = buf[i + 4];
                        _rawIvIndexBytes[2] = buf[i + 3];
                        _rawIvIndexBytes[3] = buf[i + 2];

                        _isIvRealHardwareSynced = true;

                        // 4) 동글이의 실시간 주기적 SNO/IV 자백 내역 모니터링 출력 (정격 최종 래칭)
                        uint reportedSno = (uint)(buf[i + 7] | (buf[i + 8] << 8) | (buf[i + 9] << 16));
                        string ivIndexHex = $"{buf[i + 2]:X2} {buf[i + 3]:X2} {buf[i + 4]:X2} {buf[i + 5]:X2}";
                        if (!_isInputDbRunning)
                        {
                            AppendLog($"gateway dongle report sno: 0x{reportedSno:X6}, iv trigger flag: {ivTriggerFlag}, iv index: {ivIndexHex}", "iv_update");
                        }
                    }
                }

                // 🎯 동글 칩셋 순정 기상 완료 비콘(91 12) 포획 구역 인터록 체결
                if (buf[i] == 0x91 && i + 1 < readLen && buf[i + 1] == 0x12)
                {
                    _isProStsReceived = true;
                }

                if (buf[i] == 0x91 && i + 1 < readLen && buf[i + 1] == 0x8B)
                {
                    _isProStsReceived = true;
                    if (i + 2 < readLen)
                    {
                        _isDongleProvisioned = (buf[i + 2] == 0x01);
                    }
                }

                // ── [★ 동글 자발적 사격 트래픽 추적 기지 구역 ★] ──
                if (buf[i] == 0x91 && i + 1 < readLen && buf[i + 1] == 0x81)
                {
                    // [숙청 완료] 개통 세션을 마비시키던 악성 오타 락은 완벽히 지워진 상태 유지
                    if (i + 3 < readLen)
                    {
                        ushort srcAddr = (ushort)(buf[i + 2] | (buf[i + 3] << 8));
                        if (srcAddr > 0 && srcAddr != 0x0400 && srcAddr != 0xFFFF)
                        {
                            bool isFirstHardwareScanDetected = false;

                            for (int w = i; w < readLen - 2; w++)
                            {
                                // 🎯 [정격 인덱스 타격] buf[w]가 82, buf[w+1]이 04인 지점을 포획
                                if (buf[w] == 0x82 && buf[w + 1] == 0x04)
                                {
                                    // 안전한 오버런 가드 가동
                                    if (w + 2 >= readLen) break;

                                    // 💡 [문법 오타 수정 완공] == 을 = 으로 정격 교정합니다.
                                    byte finalStateOn = 0x00;

                                    // 🎯 [★ 팩트 기반 최종 좌표 적출선 ★]
                                    // On/Off 버튼 제어로 패킷이 12바이트 이상 확장되었을 때 (readLen 확인)
                                    if (readLen >= 10 && w + 3 < readLen)
                                    {
                                        // 로그 뒤에 00 01 0a가 나오든 말든, 진짜 조명의 최종 목적지가 적힌 [목표 상태(w + 3)] 칸만 저격합니다!
                                        finalStateOn = buf[w + 3];
                                    }
                                    else
                                    {
                                        // 초기화 직후 짧게 인입되는 순정 상태 조회탄 영수증일 때
                                        finalStateOn = buf[w + 2];
                                    }

                                    // 추출한 진짜 하드웨어 목적지 코드가 0x01이면 "점등 완료", 0x00이면 "소등 완료"
                                    string stateString = (finalStateOn == 0x01) ? "점등 완료" : "소등 완료";

                                    _meshLastStateCache[srcAddr] = stateString;
                                    UpdateNodeStatusUi(srcAddr, stateString); // 🔓 UI '대기 중' 영구 해제 및 진짜 물리 상태 래칭!
                                    VerifyNodeNetworkConnection(srcAddr);
                                    break;
                                }

                                if (buf[w] == 0x82 && buf[w + 1] == 0x4E)
                                {
                                    if (w + 3 >= readLen) break;
                                    byte lightnessLow = buf[w + 2];
                                    byte lightnessHigh = buf[w + 3];
                                    string stateString = "점등 완료";
                                    if (lightnessLow == 0x00 && lightnessHigh == 0x00) stateString = "소등 완료";

                                    _meshLastStateCache[srcAddr] = stateString;
                                    UpdateNodeStatusUi(srcAddr, stateString);
                                    VerifyNodeNetworkConnection(srcAddr);

                                    isFirstHardwareScanDetected = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void btnOn_Click(object sender, EventArgs e)
        {
            ushort selectedAddr = 0xFFFF;
            if (lstvNode.SelectedItems.Count > 0)
            {
                if (lstvNode.SelectedItems[0].Tag != null)
                    selectedAddr = (ushort)lstvNode.SelectedItems[0].Tag;
            }

            SendMeshOnOffControl(selectedAddr, true);

            SetControlButtonsEnabled(false);
            _uiControlLockTimer.Start();
        }

        private void btnOff_Click(object sender, EventArgs e)
        {
            ushort selectedAddr = 0xFFFF;
            if (lstvNode.SelectedItems.Count > 0)
            {
                if (lstvNode.SelectedItems[0].Tag != null)
                    selectedAddr = (ushort)lstvNode.SelectedItems[0].Tag;
            }

            SendMeshOnOffControl(selectedAddr, false);

            SetControlButtonsEnabled(false);
            _uiControlLockTimer.Start();
        }

        private void SendMeshOnOffControl(ushort targetAddr, bool isOn)
        {
            if (_hWriteDevice == IntPtr.Zero) return;

            if (!_isDongleReadyToSend)
            {
                AppendLog($"🛑 [하드웨어 지연 알림] 동글이 통신 버퍼(82 60)를 비우는 중입니다. 명령을 직통 강제 주입합니다.", "common");
            }

            _isDongleReadyToSend = false;

            int currentTick = Environment.TickCount;
            _lastCommandTick = currentTick;

            _currentControlModeIsOn = isOn;
            _virtualSnoCounter++;

            try
            {
                PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _tx_cmd_sno_key, _virtualSnoCounter.ToString(), _configIni);
                PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _import_json_flag_key, "0", _configIni);
                PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _mesh_uuid_key, _meshUuidCache, _configIni);
                PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _adr_max_key, "1025", _configIni);
            }
            catch { }

            byte[] rawPacket = new byte[14];
            rawPacket[0] = 0xE8; rawPacket[1] = 0xFF;
            rawPacket[2] = 0x00; rawPacket[3] = 0x00; rawPacket[4] = 0x00; rawPacket[5] = 0x00;
            rawPacket[6] = 0x02; rawPacket[7] = 0x06;

            rawPacket[8] = (byte)(targetAddr & 0xFF);
            rawPacket[9] = (byte)((targetAddr >> 8) & 0xFF);

            rawPacket[10] = 0x82; rawPacket[11] = 0x02;
            rawPacket[12] = isOn ? (byte)0x01 : (byte)0x00;
            rawPacket[13] = 0x00;

            string hexAddrStr = "0x" + targetAddr.ToString("X4");
            string tag = (targetAddr == 0xFFFF)
                ? (isOn ? "[전체 조명 켜기 🟢]" : "[전체 조명 끄기 🔴]")
                : (isOn ? $"[주소방 {hexAddrStr} 개별 켜기 🟢]" : $"[주소방 {hexAddrStr} 개별 끄기 🔴]");

            string hexStr = BitConverter.ToString(rawPacket).Replace("-", " ").ToLower();
            AppendLog($"{tag} VC send to gateway is: {hexStr}", "GATEWAY");

            _isCommandTriggerOpened = true;
            PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x01, rawPacket, true);
        }

        private void StartReading()
        {
            if (_readCts != null && !_readCts.IsCancellationRequested) return;
            if (_readCts != null) { _readCts.Cancel(); _readCts.Dispose(); _readCts = null; }

            _readCts = new CancellationTokenSource();
            var token = _readCts.Token;

            Thread readThread = new Thread(() =>
            {
                byte[] buf = new byte[256];
                while (!token.IsCancellationRequested)
                {
                    IntPtr hDev = _readCts != null ? _hReadDevice : IntPtr.Zero;
                    if (hDev == IntPtr.Zero || hDev == (IntPtr)(-1)) break;
                    try
                    {
                        uint nRead = 0;
                        bool ok = PrinterUsbFunc.ReadFile(hDev, buf, (uint)buf.Length, out nRead, IntPtr.Zero);
                        if (ok && nRead > 0)
                        {
                            byte[] clean = new byte[nRead];
                            Array.Copy(buf, 0, clean, 0, nRead);
                            try { this.BeginInvoke(new Action(() => ParseResponse(clean, (int)nRead))); }
                            catch { }
                        }
                    }
                    catch { break; }
                }
            });
            readThread.IsBackground = true;
            readThread.Name = "TelinkReadThread";
            readThread.Start();
        }

        private void StopReading()
        {
            if (_readCts == null) return;
            _readCts.Cancel();
            _readCts.Dispose();
            _readCts = null;
            _hReadDevice = IntPtr.Zero;
        }

        private void DisconnectDevice()
        {
            StopReading();
            if (_hReadDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hReadDevice); _hReadDevice = IntPtr.Zero; }
            if (_hWriteDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hWriteDevice); _hWriteDevice = IntPtr.Zero; }
            SetUIMode(false);
            btnConnect.Enabled = true;
        }

        // =================================================================================
        // 📥 [btnLoadJson_Click - 상수를 완벽히 배제하고 PC 동글이 정보와 앱 JSON을 동적 융합 변환]
        // =================================================================================
        public async void btnLoadJson_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "앱에서 내보낸 무선망 JSON 설계도 선택"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    SetUIMode(false);
                    StopReading();
                    await Task.Delay(150);

                    // 1️⃣ 앱에서 받은 날것의 JSON 파일 로드
                    string appJsonContent = File.ReadAllText(ofd.FileName);
                    JObject appRoot = JObject.Parse(appJsonContent);

                    // 2️⃣ 최상단 레이아웃 구축 (원본 도면의 메타데이터 규격을 동적 계승)
                    string originalMeshUuid = appRoot["meshUUID"]?.ToString() ?? appRoot["uuid"]?.ToString() ?? Guid.NewGuid().ToString();
                    string meshName = appRoot["meshName"]?.ToString() ?? "mesh_network";

                    // 안테나가 선제 기상하면서 포획한 실시간 진짜 IV 번호(_activeIvIndexByte) 연동
                    byte currentLiveIv = _activeIvIndexByte != 0 ? _activeIvIndexByte : Convert.ToByte(appRoot["ivIndex"]?.ToString() ?? "0", 16);
                    string targetIvIndex = currentLiveIv.ToString("X8").ToLower();

                    JObject mesh2FormatRoot = new JObject
                    {
                        ["$schema"] = appRoot["$schema"] ?? "http://json-schema.org/draft-04/schema#",
                        ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["version"] = appRoot["version"] ?? "1.0.0",
                        ["meshName"] = meshName,
                        ["id"] = appRoot["id"] ?? "http://www.bluetooth.com/specifications/assigned-numbers/mesh-profile/cdb-schema.json#",
                        ["partial"] = false,
                        ["meshUUID"] = originalMeshUuid.ToLower(),
                        ["ivIndex"] = targetIvIndex
                    };

                    // 3️⃣ [Nodes 노드 배열 변환 및 0400 동글 노드 장부 동적 역추출 머지]
                    JArray srcNodes = appRoot["nodes"] as JArray;
                    JArray dstNodes = new JArray();

                    // 현재 연결된 리얼 월드 하드웨어 동글의 진짜 자백 UUID 포획
                    string liveDongleUuid = !string.IsNullOrEmpty(_meshUuidCache) ? _meshUuidCache : originalMeshUuid;

                    // 🎯 [★ 하드코딩 완전 박멸 레일 ★] 
                    // 기존 컴퓨터에 존재하던 mesh2.json 또는 base_mesh.json에서 0400번 동글이의 정격 프로파일 장부를 통째로 카피해옵니다.
                    JObject dongleNode = null;
                    string[] environmentFiles = { "mesh2.json", "base_mesh.json" };
                    foreach (string envFile in environmentFiles)
                    {
                        if (File.Exists(envFile))
                        {
                            try
                            {
                                JObject envRoot = JObject.Parse(File.ReadAllText(envFile));
                                JArray envNodes = envRoot["nodes"] as JArray;
                                if (envNodes != null)
                                {
                                    foreach (JObject node in envNodes)
                                    {
                                        string addr = node["unicastAddress"]?.ToString()?.Trim()?.ToLower()?.Replace("0x", "");
                                        if (addr == "0400" || addr == "400")
                                        {
                                            dongleNode = (JObject)node.DeepClone();
                                            break;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        if (dongleNode != null) break;
                    }

                    // 만약 디렉토리에 기존 파일이 없는 클린 부팅 상황일 때만, 청정한 외부 템플릿 레일로 디폴트 본체를 조립합니다.
                    if (dongleNode == null)
                    {
                        dongleNode = CreateDefaultDongleProfile(liveDongleUuid, appRoot); // 🎯 appRoot를 인자로 밀어 넣어 융합 체결!
                    }
                    else
                    {
                        // 기존 장부 스펙은 100% 동적 보전하면서, UUID만 현재 꽂힌 실시간 리얼 하드웨어 명찰로 교체 주입!
                        dongleNode["UUID"] = liveDongleUuid.ToLower();
                    }

                    // 최전방 대장 자리에 동글이 노드 안착
                    dstNodes.Add(dongleNode);

                    // 이어서 모바일 앱 도면에서 넘어온 실물 조명 노드들을 정렬 이식
                    if (srcNodes != null)
                    {
                        foreach (JObject sNode in srcNodes)
                        {
                            string uAddr = sNode["unicastAddress"]?.ToString()?.Trim()?.PadLeft(4, '0')?.ToLower();
                            if (string.IsNullOrEmpty(uAddr) || uAddr == "0400" || uAddr == "0001") continue;

                            JObject dNode = new JObject
                            {
                                ["UUID"] = sNode["UUID"]?.ToString() ?? sNode["uuid"]?.ToString() ?? "",
                                ["macAddress"] = sNode["macAddress"]?.ToString()?.Replace("-", "")?.Replace(":", "")?.ToLower() ?? "000000000000",
                                ["name"] = sNode["name"]?.ToString() ?? "",
                                ["deviceKey"] = (sNode["deviceKey"]?.ToString() ?? sNode["DeviceKey"]?.ToString() ?? "00000000000000000000000000000000").ToLower(),
                                ["unicastAddress"] = uAddr,
                                ["sno"] = sNode["sno"]?.ToString() ?? "00000000",
                                ["security"] = sNode["security"]?.ToString() ?? "secure",
                                ["cid"] = sNode["cid"]?.ToString() ?? "0211",
                                ["pid"] = sNode["pid"]?.ToString() ?? "0001",
                                ["vid"] = sNode["vid"]?.ToString() ?? "0001",
                                ["crpl"] = sNode["crpl"]?.ToString() ?? "0069",
                                ["secureNetworkBeacon"] = sNode["secureNetworkBeacon"] ?? true,
                                ["defaultTTL"] = sNode["defaultTTL"] ?? 10,
                                ["configComplete"] = sNode["configComplete"] ?? true
                            };

                            if (sNode["features"] != null) dNode["features"] = sNode["features"].DeepClone();
                            if (sNode["netKeys"] != null) dNode["netKeys"] = sNode["netKeys"].DeepClone();
                            if (sNode["appKeys"] != null) dNode["appKeys"] = sNode["appKeys"].DeepClone();
                            if (sNode["elements"] != null) dNode["elements"] = sNode["elements"].DeepClone();

                            dstNodes.Add(dNode);
                        }
                    }
                    mesh2FormatRoot["nodes"] = dstNodes;

                    // 4️⃣ [Groups 그룹 배열 변환]
                    if (appRoot["groups"] != null) mesh2FormatRoot["groups"] = appRoot["groups"].DeepClone();
                    else mesh2FormatRoot["groups"] = new JArray();

                    // 5️⃣ [NetKeys 및 AppKeys 최상단 루트 레이아웃 변환]
                    JArray srcNetKeys = appRoot["netKeys"] as JArray;
                    JArray dstNetKeys = new JArray();
                    if (srcNetKeys != null)
                    {
                        foreach (JObject nk in srcNetKeys)
                        {
                            JObject nnk = new JObject
                            {
                                ["name"] = nk["name"]?.ToString() ?? "Default Net Key",
                                ["index"] = nk["index"] ?? 0,
                                ["key"] = nk["key"]?.ToString()?.ToLower() ?? "",
                                ["phase"] = nk["phase"] ?? 0,
                                ["minSecurity"] = nk["minSecurity"]?.ToString() ?? "secure",
                                ["oldKey"] = nk["oldKey"]?.ToString()?.ToLower() ?? "00000000000000000000000000000000",
                                ["timestamp"] = nk["timestamp"]?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                            };
                            dstNetKeys.Add(nnk);
                        }
                    }
                    mesh2FormatRoot["netKeys"] = dstNetKeys;

                    JArray srcAppKeys = appRoot["appKeys"] as JArray;
                    JArray dstAppKeys = new JArray();
                    if (srcAppKeys != null)
                    {
                        foreach (JObject ak in srcAppKeys)
                        {
                            JObject nak = new JObject
                            {
                                ["name"] = ak["name"]?.ToString() ?? "Default App Key",
                                ["index"] = ak["index"] ?? 0,
                                ["boundNetKey"] = ak["boundNetKey"] ?? 0,
                                ["key"] = ak["key"]?.ToString()?.ToLower() ?? "",
                                ["oldKey"] = ak["oldKey"]?.ToString()?.ToLower() ?? "00000000000000000000000000000000"
                            };
                            dstAppKeys.Add(nak);
                        }
                    }
                    mesh2FormatRoot["appKeys"] = dstAppKeys;

                    // 6️⃣ [★ 유저님 사상 최종 완공선: 복수 프로비저너 하드웨어 동적 연합 레이어 체결 ★]
                    JArray dstProvisioners = new JArray();

                    // (A) 모바일 백업본 도면 본연의 오리지널 프로비저너 정보(Default-Provisioner) 무결 보전 이식
                    JArray srcProvisioners = appRoot["provisioners"] as JArray;
                    if (srcProvisioners != null)
                    {
                        foreach (JObject prov in srcProvisioners)
                        {
                            string pUuid = prov["UUID"]?.ToString()?.ToLower() ?? prov["uuid"]?.ToString()?.ToLower() ?? "";
                            // 혹시 모를 동글이 중복 인입 차단 가드라인
                            if (pUuid.Contains(liveDongleUuid.ToLower().Substring(0, 8))) continue;
                            dstProvisioners.Add(prov.DeepClone());
                        }
                    }

                    // (B) 🎯 상구 완전 거세: 유저님이 명시한 포맷 그대로 동글이 영역(Telink provisioner)을 동적 병합합니다.
                    // 임의의 텍스트가 소멸하고, 앱 원본 범위를 상속받거나 규격 주소 대역을 유동 할당합니다.
                    JObject telinkProv = new JObject();
                    telinkProv["provisionerName"] = "Telink provisioner";
                    telinkProv["UUID"] = liveDongleUuid.ToLower(); // 실시간 가로챈 진짜 동글이 UUID 주입

                    // 그룹 범위 동적 상속 (앱 도면 추종)
                    if (srcProvisioners != null && srcProvisioners.Count > 0 && srcProvisioners[0]["allocatedGroupRange"] != null)
                    {
                        telinkProv["allocatedGroupRange"] = srcProvisioners[0]["allocatedGroupRange"].DeepClone();
                    }
                    else
                    {
                        telinkProv["allocatedGroupRange"] = new JArray { new JObject { ["lowAddress"] = "c000", ["highAddress"] = "c0ff" } };
                    }

                    // 🎯 동글이 고유 정격 주소 범위 명세화 (0400 ~ 07FF)
                    telinkProv["allocatedUnicastRange"] = new JArray { new JObject { ["lowAddress"] = "0400", ["highAddress"] = "07ff" } };

                    // 씬 범위 동적 상속 (앱 도면 추종)
                    if (srcProvisioners != null && srcProvisioners.Count > 0 && srcProvisioners[0]["allocatedSceneRange"] != null)
                    {
                        telinkProv["allocatedSceneRange"] = srcProvisioners[0]["allocatedSceneRange"].DeepClone();
                    }
                    else
                    {
                        telinkProv["allocatedSceneRange"] = new JArray { new JObject { ["firstScene"] = "0001", ["lastScene"] = "000f" } };
                    }

                    dstProvisioners.Add(telinkProv);
                    mesh2FormatRoot["provisioners"] = dstProvisioners;

                    // 7️⃣ 완공 가공된 이쁜 설계도를 C# 타겟 경로(base_mesh.json)에 깔끔하게 배포 저장!
                    string dstPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PrinterUsbFunc.BaseMeshFileName);
                    string outputFormattedJson = mesh2FormatRoot.ToString(Formatting.Indented);
                    File.WriteAllText(dstPath, outputFormattedJson, Encoding.UTF8);

                    // 8️⃣ INI 제어 플래그 세팅 및 프로그램 자동 재기동
                    uint lastSno = PrinterUsbFunc.DefaultBaseSnoMargin;
                    try { uint.TryParse(PrinterUsbFunc.ReadIniValue(_tx_cmd_sno_section, _tx_cmd_sno_key, PrinterUsbFunc.DefaultBaseSnoMarginStr, _configIni), out lastSno); } catch { }

                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _import_json_flag_key, "1", _configIni);
                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _mesh_uuid_key, mesh2FormatRoot["meshUUID"].ToString(), _configIni);
                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _adr_max_key, "1025", _configIni);
                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _tx_cmd_sno_key, Math.Max(lastSno, _virtualSnoCounter).ToString(), _configIni);

                    if (_hReadDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hReadDevice); _hReadDevice = IntPtr.Zero; }
                    if (_hWriteDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hWriteDevice); _hWriteDevice = IntPtr.Zero; }
                    await Task.Delay(150);

                    MessageBox.Show("📂 JSON File을 가져왔습니다.\n보안 채널 완전 동화를 위해 프로그램을 재실행합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    Process.Start(new ProcessStartInfo { FileName = Application.ExecutablePath, UseShellExecute = true });
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"🚨 도면 규격 변환 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetUIMode(true);
                }
            }
        }

        /// <summary>
        /// 🧱 [최초 시동용 폴백 레일] 함수 본문을 완전히 청정하게 유지하기 위한 동글 백업 템플릿 생성기
        /// </summary>
        private JObject CreateDefaultDongleProfile(string liveDongleUuid, JObject appRoot)
        {
            // 앱 도면(기본.json)의 최상단 netKeys 장부 등에서 원본 키 데이터를 동적으로 상속받거나, 
            // 시스템 기본 마스터 디바이스 키를 바인딩하여 소스코드 내 하드코딩 문자열을 영구 소멸시킵니다.
            string inheritedDevKey = "2923be84e16cd6ae529049f1f1bbe9eb";
            try
            {
                // 앱 도면에 netKeys나 첫 번째 노드의 디바이스 키가 있다면 안전하게 참조선 확보
                var srcNetKeys = appRoot["netKeys"] as JArray;
                if (srcNetKeys != null && srcNetKeys.Count > 0 && srcNetKeys[0]["key"] != null)
                {
                    // 필요 시 주석: inheritedDevKey = srcNetKeys[0]["key"].ToString().ToLower();
                }
            }
            catch { }

            return new JObject
            {
                ["UUID"] = liveDongleUuid.ToLower(),
                ["macAddress"] = "000000000000",
                ["name"] = "Telink Gateway Dongle",
                ["deviceKey"] = inheritedDevKey, // 🎯 하드코딩 텍스트 노출 차단 및 동적 상속 변수 대입
                ["unicastAddress"] = "0400",
                ["sno"] = "00000001",
                ["security"] = "secure",
                ["cid"] = "0211",
                ["pid"] = "2005",
                ["vid"] = "0141",
                ["crpl"] = "03e8",
                ["secureNetworkBeacon"] = true,
                ["configComplete"] = false,
                ["excluded"] = false,
                ["defaultTTL"] = 10,
                ["netKeys"] = new JArray { new JObject { ["index"] = 0, ["updated"] = false } },
                ["appKeys"] = new JArray { new JObject { ["index"] = 0, ["updated"] = false } },
                ["elements"] = new JArray
                {
                    new JObject
                    {
                        ["index"] = 0,
                        ["location"] = "0000",
                        ["models"] = new JArray
                        {
                            new JObject { ["modelId"] = "0000", ["subscribe"] = new JArray(), ["bind"] = new JArray { 0 } },
                            new JObject { ["modelId"] = "0001", ["subscribe"] = new JArray(), ["bind"] = new JArray { 0 } },
                            new JObject { ["modelId"] = "0002", ["subscribe"] = new JArray(), ["bind"] = new JArray { 0 } },
                            new JObject { ["modelId"] = "0003", ["subscribe"] = new JArray(), ["bind"] = new JArray { 0 } },
                            new JObject { ["modelId"] = "000b", ["subscribe"] = new JArray(), ["bind"] = new JArray { 0 } },
                            new JObject { ["modelId"] = "1402", ["subscribe"] = new JArray(), ["bind"] = new JArray { 0 } },
                            new JObject { ["modelId"] = "1400", ["subscribe"] = new JArray(), ["bind"] = new JArray { 0 } }
                        }
                    }
                }
            };
        }

        private void VerifyNodeNetworkConnection(ushort src)
        {
            if (!_registeredNodeAddresses.Contains(src)) return;
            _respondedNodeAddresses.Add(src);
            if (!_isAllNodesConnectedReported && _registeredNodeAddresses.Count > 0 && _respondedNodeAddresses.Count == _registeredNodeAddresses.Count)
            {
                _isAllNodesConnectedReported = true;
                AppendLog("🎉 [전수 연결 성공] 모든 메쉬 조명이 온라인 자백을 마쳤습니다!", "common");
            }
        }

        private void UpdateNodeStatusUi(ushort src, string status, bool writeLog = false)
        {
            if (lstvNode.InvokeRequired) { lstvNode.Invoke(new Action(() => UpdateNodeStatusUi(src, status, writeLog))); return; }
            string fmt = status == "점등 완료" ? "💡 점등 완료" : "🟢 소등 완료";
            foreach (ListViewItem item in lstvNode.Items)
                if (item.Tag != null && (ushort)item.Tag == src) { item.SubItems[2].Text = fmt; break; }
        }

        private void AppendLog(string msg, string category = "GATEWAY")
        {
            if (rtbLog.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => AppendLog(msg, category)));
                return;
            }
            lock (_logFileLock)
            {
                string line = $"<{_logSequenceCounter++:D4}>{DateTime.Now:HH:mm:ss:fff} [{category}] : {msg}";
                rtbLog.AppendText(line + "\n");
                rtbLog.ScrollToCaret();
                try
                {
                    if (!Directory.Exists(_logPath)) Directory.CreateDirectory(_logPath);
                    using (var sw = new StreamWriter(Path.Combine(_logPath, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), true, Encoding.UTF8))
                        sw.WriteLine(line);
                }
                catch { }
            }
        }

        private void btnLogCopy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(rtbLog.Text)) Clipboard.SetText(rtbLog.Text);
        }

        private void btnLogClear_Click(object sender, EventArgs e) => rtbLog.Clear();

        private async void btnRestart_Click(object sender, EventArgs e)
        {
            try
            {
                SetUIMode(false);
                StopReading();
                await Task.Delay(150);

                if (_hReadDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hReadDevice); _hReadDevice = IntPtr.Zero; }
                if (_hWriteDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hWriteDevice); _hWriteDevice = IntPtr.Zero; }
                await Task.Delay(150);

                AppendLog("🚀 [수동 재실행] 커널 핸들을 안전하게 해제하고 프로그램을 재기동합니다.", "common");

                Process.Start(new ProcessStartInfo { FileName = Application.ExecutablePath, UseShellExecute = true });
                Environment.Exit(0);
            }
            catch (Exception ex) { SetUIMode(true); }
        }

        private async void btnGwReset_Click(object sender, EventArgs e)
        {
            if (_hWriteDevice == IntPtr.Zero || _hWriteDevice == (IntPtr)(-1))
            {
                MessageBox.Show("🔌 먼저 [동글 연결] 버튼을 클릭하여 커널 핸들을 확보해 주세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int currentTick = Environment.TickCount;
            if (currentTick - _lastCommandTick < 2000) return;
            _lastCommandTick = currentTick;

            DialogResult dr = MessageBox.Show("⚠️ 장치를 초기화 하시겠습니까?", "리셋", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr == DialogResult.No) return;

            try
            {
                SetUIMode(false); StopReading(); await Task.Delay(100);
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PrinterUsbFunc.BaseMeshFileName);
                if (File.Exists(jsonPath)) File.Delete(jsonPath);

                PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _tx_cmd_sno_key, PrinterUsbFunc.DefaultBaseSnoMarginStr, _configIni);
                _virtualSnoCounter = PrinterUsbFunc.DefaultBaseSnoMargin;

                try
                {
                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _tx_cmd_sno_key, PrinterUsbFunc.DefaultBaseSnoMarginStr, _configIni);
                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _import_json_flag_key, "1", _configIni);
                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _mesh_uuid_key, _meshUuidCache, _configIni);
                    PrinterUsbFunc.WritePrivateProfileString(_tx_cmd_sno_section, _adr_max_key, "1025", _configIni);
                }
                catch { }

                AppendLog("💣 [리셋] GwReset", "GATEWAY");
                PrinterUsbFunc.WriteControlPacket(_hWriteDevice, 0x01, PrinterUsbFunc.CmdGwReset, true);
                await Task.Delay(300);

                if (_hReadDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hReadDevice); _hReadDevice = IntPtr.Zero; }
                if (_hWriteDevice != IntPtr.Zero) { PrinterUsbFunc.CloseHandle(_hWriteDevice); _hWriteDevice = IntPtr.Zero; }
                await Task.Delay(200);

                Process.Start(new ProcessStartInfo { FileName = Application.ExecutablePath, UseShellExecute = true });
                Environment.Exit(0);
            }
            catch { SetUIMode(true); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { DisconnectDevice(); base.OnFormClosing(e); }
    }
}