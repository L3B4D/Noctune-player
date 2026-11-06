using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal partial class Player {
    ArtButton albumShuffle,albumSave,albumMore;
    bool AlbumSaved(){return displayedCollection!=null&&featuresState.SavedAlbums.Any(t=>Key(t)==Key(displayedCollection));}
    void ToggleAlbumSave(){
        if(displayedCollection==null||(displayedCollection.Kind=="Artist"||displayedCollection.Kind=="Local playlist"))return;
        if(AlbumSaved())featuresState.SavedAlbums.RemoveAll(t=>Key(t)==Key(displayedCollection));else featuresState.SavedAlbums.Add(displayedCollection);
        SaveFeatures();albumSave.Invalidate();
    }
    Track[] AlbumQueue(bool random){
        var source=displayedCollection!=null&&displayedCollection.Kind=="Album"?collectionContents:collectionRows.Items.Cast<ListViewItem>().Select(x=>(Track)x.Tag).ToList();var songs=source.Where(t=>string.IsNullOrEmpty(t.Kind)).ToArray();
        if(random)for(int i=songs.Length-1;i>0;i--){int j=order.Next(i+1);var t=songs[i];songs[i]=songs[j];songs[j]=t;}
        return songs;
    }
    void PlayAlbum(bool random){var songs=AlbumQueue(random);if(songs.Length==0)return;int first=queue.Count;shuffle=false;foreach(var t in songs)Add(t,false);Play(first);}
    void AlbumMenu(){
        var m=new DarkMenu{BackColor=Theme.Card,ForeColor=Theme.Paper};if(editingPlaylist!=null)m.Items.Add("Edit playlist…",null,delegate{ManagePlaylist();});
        m.Items.Add("Add tracks to queue",null,delegate{foreach(var t in AlbumQueue(false))Add(t,false);});
        m.Items.Add("Save as playlist…",null,delegate{var p=NewPlaylist();if(p==null)return;p.Songs=AlbumQueue(false).ToList();SaveFeatures();});
        m.Items.Add("Go to artist",null,delegate{OpenCollectionArtist();});
        m.Closed+=delegate{BeginInvoke((Action)m.Dispose);};m.Show(albumMore,new Point(0,albumMore.Height));
    }
    void BuildAlbumActions(){
        albumShuffle=Make(collectionPage,"Shuffle album",delegate{PlayAlbum(true);},delegate(Graphics g,Rectangle r){Fill(g,r,17,Theme.Card);Glyph(g,"\uE8B1",r,Theme.Paper,15);});
        albumSave=Make(collectionPage,"Save album to collections",ToggleAlbumSave,delegate(Graphics g,Rectangle r){Fill(g,r,17,Theme.Card);Glyph(g,AlbumSaved()?"\uEB52":"\uEB51",new Rectangle(7,0,25,r.Height),AlbumSaved()?Theme.Peach:Theme.Paper,13);TextAt(g,AlbumSaved()?"Saved":"Save",new Rectangle(34,0,r.Width-36,r.Height),10,Theme.Paper);});
        albumMore=Pill(collectionPage,"•••",AlbumMenu);
        collectionRows.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left||collectionRows.ClientSize.Width<700)return;var item=collectionRows.GetItemAt(e.X,e.Y);if(item==null)return;var t=(Track)item.Tag;if(!string.IsNullOrEmpty(t.Kind))return;int right=item.Bounds.Right-20;if(e.X>=right-28){ShowTrackMenu(t,collectionRows,e.Location);}else if(e.X>=right-62){int n=liked.FindIndex(x=>Key(x)==Key(t));if(n<0){MarkLibraryAdded(t);liked.Add(t);}else liked.RemoveAt(n);SaveLibrary();RefreshSurfaces();collectionRows.Invalidate();}};
    }
}
