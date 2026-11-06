using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
internal partial class Player {
    Surface collectionGallery;
    readonly List<ArtButton> collectionCards=new List<ArtButton>();
    bool IsMoodCollection {get{return displayedCollection!=null&&displayedCollection.Kind=="Mood";}}
    void BuildCollectionGallery(){
        collectionGallery=new Surface{BackColor=Theme.Ink,AutoScroll=true};collectionPage.Controls.Add(collectionGallery);DarkScrollHost.Wrap(collectionGallery);collectionGallery.Resize+=delegate{LayoutCollectionCards();};
    }
    void RefreshCollectionGallery(){
        if(collectionGallery==null)return;
        foreach(var card in collectionCards)card.Dispose();collectionCards.Clear();
        collectionGallery.AutoScrollPosition=Point.Empty;
        if(IsMoodCollection)foreach(ListViewItem row in collectionRows.Items){var t=(Track)row.Tag;ArtButton card=null;card=Make(collectionGallery,t.Title,delegate{OpenCollection(t);},delegate(Graphics g,Rectangle r){int d=r.Width-16;Fill(g,new Rectangle(0,0,r.Width,r.Height),14,Motion.Mix(Theme.Ink,Theme.Card,card.HoverAmount));Artwork(g,Cover(t),new Rectangle(8,8,d,d),12);HoverFinish(g,new Rectangle(8,8,d,d),12,card.HoverAmount,false);TextAt(g,t.Title,new Rectangle(8,d+17,d,28),12,Theme.Paper,true);TextAt(g,(t.Kind??"Collection")+" · YouTube Music",new Rectangle(8,d+48,d,22),9,Theme.Muted);});card.Tag=t;card.AnimateHover=true;collectionCards.Add(card);FetchArtwork(t);}
        LayoutCollectionCards();
    }
    void LayoutCollectionCards(){
        if(collectionGallery==null||!IsMoodCollection)return;
        int w=collectionGallery.ClientSize.Width,cols=Math.Max(1,w/190),gap=14,cw=Math.Max(100,(w-16-gap*(cols-1))/cols),ch=cw+73;
        var offset=collectionGallery.AutoScrollPosition;
        for(int i=0;i<collectionCards.Count;i++)collectionCards[i].SetBounds(8+(i%cols)*(cw+gap)+offset.X,8+(i/cols)*(ch+gap)+offset.Y,cw,ch);
        collectionGallery.AutoScrollMinSize=new Size(0,16+((collectionCards.Count+cols-1)/cols)*(ch+gap));
    }
}
