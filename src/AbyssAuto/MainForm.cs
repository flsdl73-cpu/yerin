using System.Diagnostics;
using System.Text.Json;
namespace AbyssAuto;
public sealed class MainForm : Form
{
    private readonly ComboBox destination=new() {DropDownStyle=ComboBoxStyle.DropDownList,Width=190};
    private readonly Button start=new() {Text="시작",Width=100};
    private readonly Button stop=new() {Text="정지 · F10",Width=120,BackColor=Color.IndianRed,ForeColor=Color.White};
    private readonly Label status=new() {Text="화면 자료를 등록하고 인식을 검증해주세요.",AutoSize=false,Height=80,Dock=DockStyle.Top,Font=new Font("맑은 고딕",18,FontStyle.Bold)};
    private readonly Label metrics=new() {AutoSize=true,Text="완료 0판  |  경과 00:00  |  평균 —"};
    private readonly CheckBox skip=new() {Text="장면·대화 넘기기",Checked=false,AutoSize=true};
    private readonly CheckBox resume=new() {Text="현재 화면 재판별 후 시작",AutoSize=true};
    private readonly NumericUpDown limit=new() {Minimum=0,Maximum=99999,Value=1,Width=75};
    private readonly TextBox logs=new() {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
    private readonly System.Windows.Forms.Timer clock=new() {Interval=500};
    private readonly Stopwatch elapsed=new();
    private CancellationTokenSource? cancellation;
    private int count;
    private readonly List<Control> locked=[];
    public MainForm()
    {
        Text=$"어비스 오토 · {Application.ProductVersion.Split('+')[0]} · 화면 검증 우선";Width=830;Height=570;MinimumSize=new(780,530);
        Font=new Font("맑은 고딕",10);BackColor=Color.FromArgb(245,247,245);
        destination.Items.AddRange(["허상의 정박지","광기의 동굴","흩어진 물길"]);destination.SelectedIndex=0;
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(20),RowCount=5,ColumnCount=1};
        root.RowStyles.Add(new(SizeType.Absolute,60));root.RowStyles.Add(new(SizeType.Absolute,90));root.RowStyles.Add(new(SizeType.Absolute,80));root.RowStyles.Add(new(SizeType.Absolute,60));root.RowStyles.Add(new(SizeType.Percent,100));
        var top=new FlowLayoutPanel {Dock=DockStyle.Fill};top.Controls.AddRange([new Label {Text="목적지",AutoSize=true},destination,start,stop]);
        var settings=new FlowLayoutPanel {Dock=DockStyle.Fill};settings.Controls.AddRange([new Label {Text="목표 횟수 (0=무제한)",AutoSize=true},limit,skip,resume,new Label {Text="부활·음식·재접속: 자료 미등록 / 비활성",AutoSize=true,ForeColor=Color.DimGray}]);
        var tools=new FlowLayoutPanel {Dock=DockStyle.Fill};
        var import=new Button {Text="화면·템플릿 등록",AutoSize=true};var inspect=new Button {Text="저장 화면 인식 검사",AutoSize=true};
        var folder=new Button {Text="설정 폴더",AutoSize=true};var detail=new CheckBox {Text="상세 로그",Checked=true,AutoSize=true};
        tools.Controls.AddRange([import,inspect,folder,detail]);root.Controls.Add(top);root.Controls.Add(status);root.Controls.Add(settings);root.Controls.Add(tools);
        var bottom=new Panel {Dock=DockStyle.Fill};metrics.Dock=DockStyle.Top;bottom.Controls.Add(logs);bottom.Controls.Add(metrics);root.Controls.Add(bottom);Controls.Add(root);
        locked.AddRange([destination,limit,skip,resume,import,inspect]);
        start.Click+=async (_,_)=>await Start();stop.Click+=(_,_)=>Stop();import.Click+=(_,_)=>Import();inspect.Click+=async (_,_)=>await Inspect();
        detail.CheckedChanged+=(_,_)=>logs.Visible=detail.Checked;
        folder.Click+=(_,_)=>Process.Start(new ProcessStartInfo("explorer.exe",AppContext.BaseDirectory){UseShellExecute=true});
        clock.Tick+=(_,_)=>metrics.Text=$"완료 {count}판  |  경과 {elapsed.Elapsed:hh\\:mm\\:ss}  |  평균 {(count==0?"—":TimeSpan.FromTicks(elapsed.Elapsed.Ticks/count).ToString(@"mm\:ss"))}";
        clock.Start();FormClosing+=(_,e)=>{ if(cancellation!=null) {Stop();e.Cancel=true;status.Text="입력 해제 후 다시 닫아주세요.";} };
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if(!Native.RegisterHotKey(Handle,10,0,0x79)) {start.Enabled=false;status.Text="F10 등록 실패: 다른 프로그램의 F10 단축키를 해제하고 다시 실행하세요.";}
    }
    protected override void OnHandleDestroyed(EventArgs e) {Native.UnregisterHotKey(Handle,10);base.OnHandleDestroyed(e);}
    protected override void WndProc(ref Message m) {if(m.Msg==0x0312 && m.WParam==10) Stop();base.WndProc(ref m);}
    private void Stop() {cancellation?.Cancel();status.Text="사용자 정지 요청";}
    private void Log(string text)
    {
        if(IsDisposed)return;
        if(InvokeRequired) {BeginInvoke(()=>Log(text));return;}
        logs.AppendText(text+Environment.NewLine);if(logs.TextLength>60000) logs.Text=logs.Text[^40000..];
    }
    private async Task Start()
    {
        if(cancellation!=null)return;
        try
        {
            var config=new Configuration();var window=new GameWindow(config.Scenario);var vision=new Vision(config);var journal=new Journal();journal.Message+=Log;
            var engine=new Engine(config,window,vision,journal);
            engine.Changed+=stage=>BeginInvoke(()=>status.Text=StageName(stage));
            engine.Completed+=(number,_)=>BeginInvoke(()=>count=number);
            var target=new[]{"husang","kwanggi","moolgil"}[destination.SelectedIndex];
            // Other destinations need their own battle evidence, not the Husang objective.
            if(target!="husang")throw new InvalidOperationException("광기의 동굴·흩어진 물길은 실제 전투 화면과 영역 검증 후 지원합니다.");
            cancellation=new();var token=cancellation.Token;var assist=skip.Checked;var maximum=(int)limit.Value;var recover=resume.Checked;
            foreach(var control in locked)control.Enabled=false;start.Enabled=false;count=0;elapsed.Restart();
            await Task.Run(()=>engine.Run(target,assist,maximum,recover,token));
            status.Text="정지됨 · 상세 로그를 확인하세요.";
        }
        catch(Exception error) {Log(error.ToString());status.Text=error.Message;}
        finally {elapsed.Stop();cancellation?.Dispose();cancellation=null;foreach(var control in locked)control.Enabled=true;start.Enabled=true;}
    }
    private static string StageName(Stage stage)=>stage switch {
        Stage.Menu=>"메뉴 열림 확인",Stage.Abyss=>"어비스 선택",Stage.Destination=>"목적지 선택",Stage.Enter=>"입장 확인",Stage.Battle=>"전투가 끝나기를 기다리고 있어요",Stage.Result=>"결과 확인",Stage.Retry=>"다시 하기",_=>stage.ToString()};
    private void Import()
    {
        try
        {
            using var picker=new OpenFileDialog {Filter="PNG 전체 화면|*.png",Title="1297×873 전체 화면 선택"};if(picker.ShowDialog()!=DialogResult.OK)return;
            using var frame=new Bitmap(picker.FileName);var config=new Configuration();
            if(frame.Width!=config.Scenario.Width||frame.Height!=config.Scenario.Height)throw new InvalidOperationException("제목 표시줄을 포함한 1297×873 원본 PNG를 선택하세요.");
            using var dialog=new Form {Text="사진 역할 확인",Width=450,Height=190,StartPosition=FormStartPosition.CenterParent};
            var choice=new ComboBox {Left=20,Top=20,Width=390,DropDownStyle=ComboBoxStyle.DropDownList};
            string[] keys=["abyss","husang","kwanggi","moolgil","enter","result"];
            choice.Items.AddRange(["메뉴의 어비스 아이콘·글자","허상의 정박지 배너","광기의 동굴 배너","흩어진 물길 배너","입장하기 버튼","결과 화면 터치 안내"]);choice.SelectedIndex=0;
            var button=new Button {Text="해당 영역 미리보기",Left=20,Top=65,Width=390,DialogResult=DialogResult.OK};dialog.Controls.AddRange([choice,button]);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;var key=keys[choice.SelectedIndex];
            using var crop=frame.Clone(config.Targets[key].Region.Rectangle,frame.PixelFormat);
            using var preview=new Form {Text="실제 사진 역할을 확인한 후 저장",Width=650,Height=360,StartPosition=FormStartPosition.CenterParent};
            var image=new PictureBox {Image=crop,Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom};
            var save=new Button {Text="내용과 역할 확인 · 템플릿 저장",Dock=DockStyle.Bottom,Height=45,DialogResult=DialogResult.OK};preview.Controls.Add(image);preview.Controls.Add(save);
            if(preview.ShowDialog(this)!=DialogResult.OK)return;
            var path=Path.Combine(config.Root,"images",config.Targets[key].Images[0]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(File.Exists(path)&&MessageBox.Show("기존 템플릿을 덮어쓸까요?","확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            crop.Save(path,System.Drawing.Imaging.ImageFormat.Png);Log($"사진 저장: {path}");
        }
        catch(Exception error){MessageBox.Show(error.Message);}
    }
    private async Task Inspect()
    {
        try
        {
            using var picker=new OpenFileDialog {Filter="PNG 전체 화면|*.png",Multiselect=true};if(picker.ShowDialog()!=DialogResult.OK)return;
            var config=new Configuration();var vision=new Vision(config);
            foreach(var file in picker.FileNames)
            {
                using var frame=new Bitmap(file);using var annotated=new Bitmap(frame);using var graphics=Graphics.FromImage(annotated);
                var results=new List<Detection>();
                foreach(var name in config.Targets.Keys)
                {
                    var result=await vision.Detect(frame,name,CancellationToken.None);results.Add(result);
                    using var pen=new Pen(result.Passed?Color.Lime:Color.OrangeRed,2);graphics.DrawRectangle(pen,config.Targets[name].Region.Rectangle);
                    if(result.Passed)graphics.DrawString(name,Font,Brushes.Lime,result.Bounds.Location);
                    Log($"{Path.GetFileName(file)} / {name}: {result.Passed}, 점수 {result.Score}, 사진 {result.ImageScore:F3}, OCR [{result.Ocr}], {result.Reason}");
                }
                var output=Path.Combine(config.Root,"captures","offline");Directory.CreateDirectory(output);
                var nameOut=Path.Combine(output,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                annotated.Save(nameOut+".png",System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText(nameOut+".json",JsonSerializer.Serialize(results,Configuration.JsonOptions));
            }
            status.Text="저장 화면 검사 완료 · 로그와 captures/offline 확인";
        }
        catch(Exception error){Log(error.ToString());status.Text=error.Message;}
    }
}
