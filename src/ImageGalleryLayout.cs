using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    public sealed class ImageGalleryPlacement
    {
        public double X {get;internal set;}
        public double Y {get;internal set;}
        public double Width {get;internal set;}
        public double Height {get;internal set;}
    }
    public sealed class ImageGalleryLayoutResult
    {
        public IList<ImageGalleryPlacement> Items {get;internal set;}
        public double Height {get;internal set;}
        public int Columns {get;internal set;}
    }
    public static class ImageGalleryLayout
    {
        public const double Gap=16;
        public const double TargetColumnWidth=220;
        public static ImageGalleryLayoutResult Calculate(IList<GeneratedImage> images,double availableWidth,string mode)
        {
            // UI measure passes can report infinity before the containing viewport is known.
            // Bound unrealistically large finite widths as well to keep geometry and allocation finite.
            double width=Double.IsNaN(availableWidth) || Double.IsInfinity(availableWidth)?1:Math.Max(1,Math.Min(1000000,availableWidth));
            int columns=Math.Max(1,(int)Math.Floor((width+Gap)/(TargetColumnWidth+Gap)));
            double columnWidth=(width-Gap*(columns-1))/columns;
            var placements=new List<ImageGalleryPlacement>();var result=new ImageGalleryLayoutResult{Items=placements.AsReadOnly(),Height=0,Columns=columns};
            if(images==null || images.Count==0)return result;
            bool grid=String.Equals(mode,"grid",StringComparison.Ordinal);double[] nextY=new double[columns];
            for(int index=0;index<images.Count;index++){
                int column;
                if(grid)column=index%columns;
                else{column=0;for(int candidate=1;candidate<columns;candidate++)if(nextY[candidate]<nextY[column])column=candidate;}
                var image=images[index];double height=columnWidth;
                if(!grid && image!=null && image.Width>0 && image.Height>0)height=columnWidth*((double)image.Height/image.Width);
                double y=grid?(index/columns)*(columnWidth+Gap):nextY[column];
                var placement=new ImageGalleryPlacement{X=column*(columnWidth+Gap),Y=y,Width=columnWidth,Height=height};placements.Add(placement);
                nextY[column]=y+height+Gap;result.Height=Math.Max(result.Height,y+height);
            }
            return result;
        }
    }
}
