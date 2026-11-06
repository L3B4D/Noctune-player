using System;
using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Foundation;
internal partial class Player {
    [ComImport,Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a"),InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]interface IMediaInterop { SystemMediaTransportControls GetForWindow(IntPtr window,ref Guid iid); }
    [DllImport("combase.dll")]static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)]string text,int length,out IntPtr value);
    [DllImport("combase.dll")]static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")]static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,[MarshalAs(UnmanagedType.IInspectable)]out object factory);
    SystemMediaTransportControls mediaControls;string mediaKey;int mediaTick;
    void InitSystemMedia(){
        Shown+=delegate{
            IntPtr name=IntPtr.Zero;
            try{string type="Windows.Media.SystemMediaTransportControls";Marshal.ThrowExceptionForHR(WindowsCreateString(type,type.Length,out name));Guid iid=typeof(IMediaInterop).GUID;object factory;Marshal.ThrowExceptionForHR(RoGetActivationFactory(name,ref iid,out factory));Guid controlsId=new Guid("99fa3ff4-1742-42a6-902e-087d41f965ec");mediaControls=((IMediaInterop)factory).GetForWindow(Handle,ref controlsId);
                mediaControls.IsEnabled=true;mediaControls.IsPlayEnabled=true;mediaControls.IsPauseEnabled=true;mediaControls.IsNextEnabled=true;mediaControls.IsPreviousEnabled=true;
                mediaControls.ButtonPressed+=MediaPressed;mediaControls.PlaybackPositionChangeRequested+=MediaSeek;
            }catch(COMException){}catch(TypeLoadException){}finally{if(name!=IntPtr.Zero)WindowsDeleteString(name);}
        };
        timer.Tick+=delegate{if(mediaControls==null||closing)return;try{mediaControls.PlaybackStatus=active?(audio.State==3?MediaPlaybackStatus.Playing:MediaPlaybackStatus.Paused):MediaPlaybackStatus.Stopped;
            string key=index>=0?Key(Current):null;if(++mediaTick%4==0&&active&&audio.Length>0)mediaControls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties{StartTime=TimeSpan.Zero,MinSeekTime=TimeSpan.Zero,EndTime=TimeSpan.FromMilliseconds(audio.Length),MaxSeekTime=TimeSpan.FromMilliseconds(audio.Length),Position=TimeSpan.FromMilliseconds(Math.Max(0,audio.Time))});if(key!=mediaKey){mediaKey=key;var updater=mediaControls.DisplayUpdater;updater.Type=MediaPlaybackType.Music;updater.MusicProperties.Title=index>=0?(Current.Title??""):"Noctune";updater.MusicProperties.Artist=index>=0?(Current.Artist??""):"";updater.Update();}
        }catch(COMException){}};
        FormClosing+=delegate{if(mediaControls!=null){mediaControls.ButtonPressed-=MediaPressed;mediaControls.PlaybackPositionChangeRequested-=MediaSeek;mediaControls.IsEnabled=false;}};
    }
    void MediaSeek(SystemMediaTransportControls sender,PlaybackPositionChangeRequestedEventArgs e){if(!closing)BeginInvoke((Action)delegate{if(active&&audio.Seekable)SeekTo((long)e.RequestedPlaybackPosition.TotalMilliseconds);});}
    void MediaPressed(SystemMediaTransportControls sender,SystemMediaTransportControlsButtonPressedEventArgs e){if(closing)return;BeginInvoke((Action)delegate{switch(e.Button){case SystemMediaTransportControlsButton.Play:if(!active||audio.State!=3)Toggle();break;case SystemMediaTransportControlsButton.Pause:if(active)PausePlayback(true);break;case SystemMediaTransportControlsButton.Next:Step(1);break;case SystemMediaTransportControlsButton.Previous:Step(-1);break;}});}
}


