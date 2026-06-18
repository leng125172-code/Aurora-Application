namespace AuroraStruct3D.OpenCV.Attributes
{
    internal class SubCategoryAttribute : Attribute
    {
        public SubCategoryAttribute(string v)
        {
            V = v;
        }

        public string V { get; }
    }
}
