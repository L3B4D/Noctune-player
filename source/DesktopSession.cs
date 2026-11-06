using System;using System.Drawing;using System.Windows.Forms;using System.Linq;
internal partial class Player {
    NotifyIcon tray;
    void InitDesktopSession(){
        tray=new NotifyIcon{Icon=Icon,Text="Noctune",Visible=true};var menu=new DarkMenu{BackColor=Theme.Card,ForeColor=Theme.Paper};menu.Items.Add("Open Noctune",null,delegate{Show();WindowState=FormWindowState.Normal;Activate();});menu.Items.Add("Play / pause",null,delegate{Toggle();});menu.Items.Add("Previous",null,delegate{Step(-1);});menu.Items.Add("Next",null,delegate{Step(1);});menu.Items.Add("Exit",null,delegate{Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{Show();WindowState=FormWindowState.Normal;Activate();};
        Resize+=delegate{if(WindowState==FormWindowState.Minimized&&featuresState.MinimizeToTray)Hide();};FormClosed+=delegate{tray.Dispose();menu.Dispose();};
        Shown+=delegate{
            if(featuresState.Queue!=null){foreach(var t in featuresState.Queue.Where(t=>t!=null&&Key(t)!=null&&string.IsNullOrEmpty(t.Kind)))Add(t,false);index=Math.Max(-1,Math.Min(featuresState.Index,queue.Count-1));restoredPosition=featuresState.Position;if(index>=0){now.Text=Current.Title;status.Text="Ready to resume · "+Current.Artist;FetchArtwork(Current);mini.Invalidate(true);}}
            volume.Value=Math.Max(volume.Minimum,Math.Min(volume.Maximum,featuresState.Volume));
        };
        InitSystemMedia();
    }
    protected override void WndProc(ref Message m){
        if(m.Msg==0x319){int command=((int)m.LParam.ToInt64()>>16)&0x7ff;switch(command){case 14:Toggle();m.Result=new IntPtr(1);return;case 11:Step(1);m.Result=new IntPtr(1);return;case 12:Step(-1);m.Result=new IntPtr(1);return;case 13:if(active)PausePlayback(true);m.Result=new IntPtr(1);return;}}
        base.WndProc(ref m);
    }
}
