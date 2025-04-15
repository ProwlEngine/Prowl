namespace Prowl.PaperUI.Graphics
{
    internal enum CommandType
	{
		MoveTo = 0,
		LineTo = 1,
		BezierTo = 2,
		Close = 3,
		Winding = 4,
	};

	internal enum PointFlags
	{
		Corner = 0x01,
		Left = 0x02,
		Bevel = 0x04,
		InnerBevel = 0x08,
	};

	internal enum CodepointType
	{
		Space,
		Newline,
		Char,
		CjkChar,
	};
}