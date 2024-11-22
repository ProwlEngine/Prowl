using System.Globalization;

namespace Prowl.Runtime.Cloning.Surrogates
{
    public class CultureInfoSurrogate : CloneSurrogate<CultureInfo>
	{
        protected override bool IsImmutableTarget => true;

        public override void CreateTargetObject(CultureInfo source, ref CultureInfo target, ICloneTargetSetup setup)
		{
			// We'll just use the predefined Clone method that CultureInfo already provides
			if (source == null)
				target = null;
			else
				target = source.Clone() as CultureInfo;
		}

		public override void SetupCloneTargets(CultureInfo source, CultureInfo target, ICloneTargetSetup setup)
		{
			// Nothing to investigate or set up
		}

		public override void CopyDataTo(CultureInfo source, CultureInfo target, ICloneOperation operation)
		{
			// Nothing to copy.
		}
	}
}
