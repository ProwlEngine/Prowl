using System;
using System.Numerics;

namespace Scribe.Internal
{
    class Interpreter
    {
        GraphicsState state;
        GraphicsState cvtState;
        ExecutionStack stack;
        InstructionStream[] functions;
        InstructionStream[] instructionDefs;
        float[] controlValueTable;
        int[] storage;
        int[] contours;
        float scale;
        int ppem;
        int callStackSize;
        float fdotp;
        float roundThreshold;
        float roundPhase;
        float roundPeriod;
        Zone zp0, zp1, zp2;
        Zone points, twilight;

        public Interpreter(int maxStack, int maxStorage, int maxFunctions, int maxInstructionDefs, int maxTwilightPoints)
        {
            stack = new ExecutionStack(maxStack);
            storage = new int[maxStorage];
            functions = new InstructionStream[maxFunctions];
            instructionDefs = new InstructionStream[maxInstructionDefs > 0 ? 256 : 0];
            state = new GraphicsState();
            cvtState = new GraphicsState();
            twilight = new Zone(new PointF[maxTwilightPoints], isTwilight: true);
        }

        public void InitializeFunctionDefs(byte[] instructions) => Execute(new InstructionStream(instructions), false, true);

        public void SetControlValueTable(FUnit[] cvt, float scale, float ppem, byte[] cvProgram)
        {
            if (this.scale == scale || cvt == null)
                return;

            if (controlValueTable == null)
                controlValueTable = new float[cvt.Length];
            for (int i = 0; i < cvt.Length; i++)
                controlValueTable[i] = cvt[i] * scale;

            this.scale = scale;
            this.ppem = (int)Math.Round(ppem);
            zp0 = zp1 = zp2 = points;
            state.Reset();
            stack.Clear();

            if (cvProgram != null)
            {
                Execute(new InstructionStream(cvProgram), false, false);

                // save off the CVT graphics state so that we can restore it for each glyph we hint
                if ((state.InstructionControl & InstructionControlFlags.UseDefaultGraphicsState) != 0)
                    cvtState.Reset();
                else
                {
                    // always reset a few fields; copy the reset
                    cvtState = state;
                    cvtState.Freedom = Vector2.UnitX;
                    cvtState.Projection = Vector2.UnitX;
                    cvtState.DualProjection = Vector2.UnitX;
                    cvtState.RoundState = RoundMode.ToGrid;
                    cvtState.Loop = 1;
                }
            }
        }

        public void HintGlyph(PointF[] glyphPoints, int[] contours, byte[] instructions)
        {
            if (instructions == null || instructions.Length == 0)
                return;

            // check if the CVT program disabled hinting
            if ((state.InstructionControl & InstructionControlFlags.InhibitGridFitting) != 0)
                return;

            // TODO: composite glyphs
            // TODO: round the phantom points?

            // save contours and points
            this.contours = contours;
            zp0 = zp1 = zp2 = points = new Zone(glyphPoints, isTwilight: false);

            // reset all of our shared state
            state = cvtState;
            callStackSize = 0;
            debugList.Clear();
            stack.Clear();
            OnVectorsUpdated();

            // normalize the round state settings
            switch (state.RoundState)
            {
                case RoundMode.Super: SetSuperRound(1.0f); break;
                case RoundMode.Super45: SetSuperRound(Sqrt2Over2); break;
            }

            Execute(new InstructionStream(instructions), false, false);
        }

        List<OpCode> debugList = new List<OpCode>();

        void Execute(InstructionStream stream, bool inFunction, bool allowFunctionDefs)
        {
            // dispatch each instruction in the stream
            while (!stream.Done)
            {
                var opcode = stream.NextOpCode();
                debugList.Add(opcode);
                switch (opcode)
                {
                    // ==== PUSH INSTRUCTIONS ====
                    case OpCode.NPUSHB:
                    case OpCode.PUSHB1:
                    case OpCode.PUSHB2:
                    case OpCode.PUSHB3:
                    case OpCode.PUSHB4:
                    case OpCode.PUSHB5:
                    case OpCode.PUSHB6:
                    case OpCode.PUSHB7:
                    case OpCode.PUSHB8:
                    {
                        var count = opcode == OpCode.NPUSHB ? stream.NextByte() : opcode - OpCode.PUSHB1 + 1;
                        for (int i = 0; i < count; i++)
                            stack.Push(stream.NextByte());
                    }
                    break;
                    case OpCode.NPUSHW:
                    case OpCode.PUSHW1:
                    case OpCode.PUSHW2:
                    case OpCode.PUSHW3:
                    case OpCode.PUSHW4:
                    case OpCode.PUSHW5:
                    case OpCode.PUSHW6:
                    case OpCode.PUSHW7:
                    case OpCode.PUSHW8:
                    {
                        var count = opcode == OpCode.NPUSHW ? stream.NextByte() : opcode - OpCode.PUSHW1 + 1;
                        for (int i = 0; i < count; i++)
                            stack.Push(stream.NextWord());
                    }
                    break;

                    // ==== STORAGE MANAGEMENT ====
                    case OpCode.RS:
                    {
                        var loc = CheckIndex(stack.Pop(), storage.Length);
                        stack.Push(storage[loc]);
                    }
                    break;
                    case OpCode.WS:
                    {
                        var value = stack.Pop();
                        var loc = CheckIndex(stack.Pop(), storage.Length);
                        storage[loc] = value;
                    }
                    break;

                    // ==== CONTROL VALUE TABLE ====
                    case OpCode.WCVTP:
                    {
                        var value = stack.PopFloat();
                        var loc = CheckIndex(stack.Pop(), controlValueTable.Length);
                        controlValueTable[loc] = value;
                    }
                    break;
                    case OpCode.WCVTF:
                    {
                        var value = stack.Pop();
                        var loc = CheckIndex(stack.Pop(), controlValueTable.Length);
                        controlValueTable[loc] = value * scale;
                    }
                    break;
                    case OpCode.RCVT: stack.Push(ReadCvt()); break;

                    // ==== STATE VECTORS ====
                    case OpCode.SVTCA0:
                    case OpCode.SVTCA1:
                    {
                        var axis = opcode - OpCode.SVTCA0;
                        SetFreedomVectorToAxis(axis);
                        SetProjectionVectorToAxis(axis);
                    }
                    break;
                    case OpCode.SFVTPV: state.Freedom = state.Projection; OnVectorsUpdated(); break;
                    case OpCode.SPVTCA0:
                    case OpCode.SPVTCA1: SetProjectionVectorToAxis(opcode - OpCode.SPVTCA0); break;
                    case OpCode.SFVTCA0:
                    case OpCode.SFVTCA1: SetFreedomVectorToAxis(opcode - OpCode.SFVTCA0); break;
                    case OpCode.SPVTL0:
                    case OpCode.SPVTL1:
                    case OpCode.SFVTL0:
                    case OpCode.SFVTL1: SetVectorToLine(opcode - OpCode.SPVTL0, false); break;
                    case OpCode.SDPVTL0:
                    case OpCode.SDPVTL1: SetVectorToLine(opcode - OpCode.SDPVTL0, true); break;
                    case OpCode.SPVFS:
                    case OpCode.SFVFS:
                    {
                        var y = stack.Pop();
                        var x = stack.Pop();
                        var vec = Vector2.Normalize(new Vector2(F2Dot14ToFloat(x), F2Dot14ToFloat(y)));
                        if (opcode == OpCode.SFVFS)
                            state.Freedom = vec;
                        else
                        {
                            state.Projection = vec;
                            state.DualProjection = vec;
                        }
                        OnVectorsUpdated();
                    }
                    break;
                    case OpCode.GPV:
                    case OpCode.GFV:
                    {
                        var vec = opcode == OpCode.GPV ? state.Projection : state.Freedom;
                        stack.Push(FloatToF2Dot14(vec.X));
                        stack.Push(FloatToF2Dot14(vec.Y));
                    }
                    break;

                    // ==== GRAPHICS STATE ====
                    case OpCode.SRP0: state.Rp0 = stack.Pop(); break;
                    case OpCode.SRP1: state.Rp1 = stack.Pop(); break;
                    case OpCode.SRP2: state.Rp2 = stack.Pop(); break;
                    case OpCode.SZP0: zp0 = GetZoneFromStack(); break;
                    case OpCode.SZP1: zp1 = GetZoneFromStack(); break;
                    case OpCode.SZP2: zp2 = GetZoneFromStack(); break;
                    case OpCode.SZPS: zp0 = zp1 = zp2 = GetZoneFromStack(); break;
                    case OpCode.RTHG: state.RoundState = RoundMode.ToHalfGrid; break;
                    case OpCode.RTG: state.RoundState = RoundMode.ToGrid; break;
                    case OpCode.RTDG: state.RoundState = RoundMode.ToDoubleGrid; break;
                    case OpCode.RDTG: state.RoundState = RoundMode.DownToGrid; break;
                    case OpCode.RUTG: state.RoundState = RoundMode.UpToGrid; break;
                    case OpCode.ROFF: state.RoundState = RoundMode.Off; break;
                    case OpCode.SROUND: state.RoundState = RoundMode.Super; SetSuperRound(1.0f); break;
                    case OpCode.S45ROUND: state.RoundState = RoundMode.Super45; SetSuperRound(Sqrt2Over2); break;
                    case OpCode.INSTCTRL:
                    {
                        var selector = stack.Pop();
                        if (selector >= 1 && selector <= 2)
                        {
                            // value is false if zero, otherwise shift the right bit into the flags
                            var bit = 1 << selector - 1;
                            if (stack.Pop() == 0)
                                state.InstructionControl = (InstructionControlFlags)((int)state.InstructionControl & ~bit);
                            else
                                state.InstructionControl = (InstructionControlFlags)((int)state.InstructionControl | bit);
                        }
                    }
                    break;
                    case OpCode.SCANCTRL: /* instruction unspported */ stack.Pop(); break;
                    case OpCode.SCANTYPE: /* instruction unspported */ stack.Pop(); break;
                    case OpCode.SANGW: /* instruction unspported */ stack.Pop(); break;
                    case OpCode.SLOOP: state.Loop = stack.Pop(); break;
                    case OpCode.SMD: state.MinDistance = stack.PopFloat(); break;
                    case OpCode.SCVTCI: state.ControlValueCutIn = stack.PopFloat(); break;
                    case OpCode.SSWCI: state.SingleWidthCutIn = stack.PopFloat(); break;
                    case OpCode.SSW: state.SingleWidthValue = stack.Pop() * scale; break;
                    case OpCode.FLIPON: state.AutoFlip = true; break;
                    case OpCode.FLIPOFF: state.AutoFlip = false; break;
                    case OpCode.SDB: state.DeltaBase = stack.Pop(); break;
                    case OpCode.SDS: state.DeltaShift = stack.Pop(); break;

                    // ==== POINT MEASUREMENT ====
                    case OpCode.GC0: stack.Push(Project(zp2.GetCurrent(stack.Pop()))); break;
                    case OpCode.GC1: stack.Push(DualProject(zp2.GetOriginal(stack.Pop()))); break;
                    case OpCode.SCFS:
                    {
                        var value = stack.PopFloat();
                        var index = stack.Pop();
                        var point = zp2.GetCurrent(index);
                        MovePoint(zp2, index, value - Project(point));

                        // moving twilight points moves their "original" value also
                        if (zp2.IsTwilight)
                            zp2.Original[index].P = zp2.Current[index].P;
                    }
                    break;
                    case OpCode.MD0:
                    {
                        var p1 = zp1.GetOriginal(stack.Pop());
                        var p2 = zp0.GetOriginal(stack.Pop());
                        stack.Push(DualProject(p2 - p1));
                    }
                    break;
                    case OpCode.MD1:
                    {
                        var p1 = zp1.GetCurrent(stack.Pop());
                        var p2 = zp0.GetCurrent(stack.Pop());
                        stack.Push(Project(p2 - p1));
                    }
                    break;
                    case OpCode.MPS: // MPS should return point size, but we assume DPI so it's the same as pixel size
                    case OpCode.MPPEM: stack.Push(ppem); break;
                    case OpCode.AA: /* deprecated instruction */ stack.Pop(); break;

                    // ==== POINT MODIFICATION ====
                    case OpCode.FLIPPT:
                    {
                        for (int i = 0; i < state.Loop; i++)
                        {
                            var index = stack.Pop();
                            if (points.Current[index].Type == PointType.OnCurve)
                                points.Current[index].Type = PointType.Quadratic;
                            else
                                points.Current[index].Type = PointType.OnCurve;
                        }
                        state.Loop = 1;
                    }
                    break;
                    case OpCode.FLIPRGON:
                    {
                        var end = stack.Pop();
                        for (int i = stack.Pop(); i <= end; i++)
                            points.Current[i].Type = PointType.OnCurve;
                    }
                    break;
                    case OpCode.FLIPRGOFF:
                    {
                        var end = stack.Pop();
                        for (int i = stack.Pop(); i <= end; i++)
                            points.Current[i].Type = PointType.Quadratic;
                    }
                    break;
                    case OpCode.SHP0:
                    case OpCode.SHP1:
                    {
                        Zone zone;
                        int point;
                        var displacement = ComputeDisplacement((int)opcode, out zone, out point);
                        ShiftPoints(displacement);
                    }
                    break;
                    case OpCode.SHPIX: ShiftPoints(stack.PopFloat() * state.Freedom); break;
                    case OpCode.SHC0:
                    case OpCode.SHC1:
                    {
                        Zone zone;
                        int point;
                        var displacement = ComputeDisplacement((int)opcode, out zone, out point);
                        var touch = GetTouchState();
                        var contour = stack.Pop();
                        var start = contour == 0 ? 0 : contours[contour - 1] + 1;
                        var count = zp2.IsTwilight ? zp2.Current.Length : contours[contour] + 1;

                        for (int i = start; i < count; i++)
                        {
                            // don't move the reference point
                            if (zone.Current != zp2.Current || point != i)
                            {
                                zp2.Current[i].P += displacement;
                                zp2.TouchState[i] |= touch;
                            }
                        }
                    }
                    break;
                    case OpCode.SHZ0:
                    case OpCode.SHZ1:
                    {
                        Zone zone;
                        int point;
                        var displacement = ComputeDisplacement((int)opcode, out zone, out point);
                        var count = 0;
                        if (zp2.IsTwilight)
                            count = zp2.Current.Length;
                        else if (contours.Length > 0)
                            count = contours[contours.Length - 1] + 1;

                        for (int i = 0; i < count; i++)
                        {
                            // don't move the reference point
                            if (zone.Current != zp2.Current || point != i)
                                zp2.Current[i].P += displacement;
                        }
                    }
                    break;
                    case OpCode.MIAP0:
                    case OpCode.MIAP1:
                    {
                        var distance = ReadCvt();
                        var pointIndex = stack.Pop();

                        // this instruction is used in the CVT to set up twilight points with original values
                        if (zp0.IsTwilight)
                        {
                            var original = state.Freedom * distance;
                            zp0.Original[pointIndex].P = original;
                            zp0.Current[pointIndex].P = original;
                        }

                        // current position of the point along the projection vector
                        var point = zp0.GetCurrent(pointIndex);
                        var currentPos = Project(point);
                        if (opcode == OpCode.MIAP1)
                        {
                            // only use the CVT if we are above the cut-in point
                            if (Math.Abs(distance - currentPos) > state.ControlValueCutIn)
                                distance = currentPos;
                            distance = Round(distance);
                        }

                        MovePoint(zp0, pointIndex, distance - currentPos);
                        state.Rp0 = pointIndex;
                        state.Rp1 = pointIndex;
                    }
                    break;
                    case OpCode.MDAP0:
                    case OpCode.MDAP1:
                    {
                        var pointIndex = stack.Pop();
                        var point = zp0.GetCurrent(pointIndex);
                        var distance = 0.0f;
                        if (opcode == OpCode.MDAP1)
                        {
                            distance = Project(point);
                            distance = Round(distance) - distance;
                        }

                        MovePoint(zp0, pointIndex, distance);
                        state.Rp0 = pointIndex;
                        state.Rp1 = pointIndex;
                    }
                    break;
                    case OpCode.MSIRP0:
                    case OpCode.MSIRP1:
                    {
                        var targetDistance = stack.PopFloat();
                        var pointIndex = stack.Pop();

                        // if we're operating on the twilight zone, initialize the points
                        if (zp1.IsTwilight)
                        {
                            zp1.Original[pointIndex].P = zp0.Original[state.Rp0].P + targetDistance * state.Freedom / fdotp;
                            zp1.Current[pointIndex].P = zp1.Original[pointIndex].P;
                        }

                        var currentDistance = Project(zp1.GetCurrent(pointIndex) - zp0.GetCurrent(state.Rp0));
                        MovePoint(zp1, pointIndex, targetDistance - currentDistance);

                        state.Rp1 = state.Rp0;
                        state.Rp2 = pointIndex;
                        if (opcode == OpCode.MSIRP1)
                            state.Rp0 = pointIndex;
                    }
                    break;
                    case OpCode.IP:
                    {
                        var originalBase = zp0.GetOriginal(state.Rp1);
                        var currentBase = zp0.GetCurrent(state.Rp1);
                        var originalRange = DualProject(zp1.GetOriginal(state.Rp2) - originalBase);
                        var currentRange = Project(zp1.GetCurrent(state.Rp2) - currentBase);

                        for (int i = 0; i < state.Loop; i++)
                        {
                            var pointIndex = stack.Pop();
                            var point = zp2.GetCurrent(pointIndex);
                            var currentDistance = Project(point - currentBase);
                            var originalDistance = DualProject(zp2.GetOriginal(pointIndex) - originalBase);

                            var newDistance = 0.0f;
                            if (originalDistance != 0.0f)
                            {
                                // a range of 0.0f is invalid according to the spec (would result in a div by zero)
                                if (originalRange == 0.0f)
                                    newDistance = originalDistance;
                                else
                                    newDistance = originalDistance * currentRange / originalRange;
                            }

                            MovePoint(zp2, pointIndex, newDistance - currentDistance);
                        }
                        state.Loop = 1;
                    }
                    break;
                    case OpCode.ALIGNRP:
                    {
                        for (int i = 0; i < state.Loop; i++)
                        {
                            var pointIndex = stack.Pop();
                            var p1 = zp1.GetCurrent(pointIndex);
                            var p2 = zp0.GetCurrent(state.Rp0);
                            MovePoint(zp1, pointIndex, -Project(p1 - p2));
                        }
                        state.Loop = 1;
                    }
                    break;
                    case OpCode.ALIGNPTS:
                    {
                        var p1 = stack.Pop();
                        var p2 = stack.Pop();
                        var distance = Project(zp0.GetCurrent(p2) - zp1.GetCurrent(p1)) / 2;
                        MovePoint(zp1, p1, distance);
                        MovePoint(zp0, p2, -distance);
                    }
                    break;
                    case OpCode.UTP: zp0.TouchState[stack.Pop()] &= ~GetTouchState(); break;
                    case OpCode.IUP0:
                    case OpCode.IUP1:
                        unsafe
                        {
                            // bail if no contours (empty outline)
                            if (contours.Length == 0)
                                break;

                            fixed (PointF* currentPtr = points.Current) fixed (PointF* originalPtr = points.Original)
                            {
                                // opcode controls whether we care about X or Y direction
                                // do some pointer trickery so we can operate on the
                                // points in a direction-agnostic manner
                                TouchState touchMask;
                                byte* current;
                                byte* original;
                                if (opcode == OpCode.IUP0)
                                {
                                    touchMask = TouchState.Y;
                                    current = (byte*)&currentPtr->P.Y;
                                    original = (byte*)&originalPtr->P.Y;
                                }
                                else
                                {
                                    touchMask = TouchState.X;
                                    current = (byte*)&currentPtr->P.X;
                                    original = (byte*)&originalPtr->P.X;
                                }

                                var point = 0;
                                for (int i = 0; i < contours.Length; i++)
                                {
                                    var endPoint = contours[i];
                                    var firstPoint = point;
                                    var firstTouched = -1;
                                    var lastTouched = -1;

                                    for (; point <= endPoint; point++)
                                    {
                                        // check whether this point has been touched
                                        if ((points.TouchState[point] & touchMask) != 0)
                                        {
                                            // if this is the first touched point in the contour, note it and continue
                                            if (firstTouched < 0)
                                            {
                                                firstTouched = point;
                                                lastTouched = point;
                                                continue;
                                            }

                                            // otherwise, interpolate all untouched points
                                            // between this point and our last touched point
                                            InterpolatePoints(current, original, lastTouched + 1, point - 1, lastTouched, point);
                                            lastTouched = point;
                                        }
                                    }

                                    // check if we had any touched points at all in this contour
                                    if (firstTouched >= 0)
                                    {
                                        // there are two cases left to handle:
                                        // 1. there was only one touched point in the whole contour, in
                                        //    which case we want to shift everything relative to that one
                                        // 2. several touched points, in which case handle the gap from the
                                        //    beginning to the first touched point and the gap from the last
                                        //    touched point to the end of the contour
                                        if (lastTouched == firstTouched)
                                        {
                                            var delta = *GetPoint(current, lastTouched) - *GetPoint(original, lastTouched);
                                            if (delta != 0.0f)
                                            {
                                                for (int j = firstPoint; j < lastTouched; j++)
                                                    *GetPoint(current, j) += delta;
                                                for (int j = lastTouched + 1; j <= endPoint; j++)
                                                    *GetPoint(current, j) += delta;
                                            }
                                        }
                                        else
                                        {
                                            InterpolatePoints(current, original, lastTouched + 1, endPoint, lastTouched, firstTouched);
                                            if (firstTouched > 0)
                                                InterpolatePoints(current, original, firstPoint, firstTouched - 1, lastTouched, firstTouched);
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case OpCode.ISECT:
                    {
                        // move point P to the intersection of lines A and B
                        var b1 = zp0.GetCurrent(stack.Pop());
                        var b0 = zp0.GetCurrent(stack.Pop());
                        var a1 = zp1.GetCurrent(stack.Pop());
                        var a0 = zp1.GetCurrent(stack.Pop());
                        var index = stack.Pop();

                        // calculate intersection using determinants: https://en.wikipedia.org/wiki/Line%E2%80%93line_intersection#Given_two_points_on_each_line
                        var da = a0 - a1;
                        var db = b0 - b1;
                        var den = da.X * db.Y - da.Y * db.X;
                        if (Math.Abs(den) <= Epsilon)
                        {
                            // parallel lines; spec says to put the ppoint "into the middle of the two lines"
                            zp2.Current[index].P = (a0 + a1 + b0 + b1) / 4;
                        }
                        else
                        {
                            var t = a0.X * a1.Y - a0.Y * a1.X;
                            var u = b0.X * b1.Y - b0.Y * b1.X;
                            var p = new Vector2(
                                t * db.X - da.X * u,
                                t * db.Y - da.Y * u
                            );
                            zp2.Current[index].P = p / den;
                        }
                        zp2.TouchState[index] = TouchState.Both;
                    }
                    break;

                    // ==== STACK MANAGEMENT ====
                    case OpCode.DUP: stack.Duplicate(); break;
                    case OpCode.POP: stack.Pop(); break;
                    case OpCode.CLEAR: stack.Clear(); break;
                    case OpCode.SWAP: stack.Swap(); break;
                    case OpCode.DEPTH: stack.Depth(); break;
                    case OpCode.CINDEX: stack.Copy(); break;
                    case OpCode.MINDEX: stack.Move(); break;
                    case OpCode.ROLL: stack.Roll(); break;

                    // ==== FLOW CONTROL ====
                    case OpCode.IF:
                    {
                        // value is false; jump to the next else block or endif marker
                        // otherwise, we don't have to do anything; we'll keep executing this block
                        if (!stack.PopBool())
                        {
                            int indent = 1;
                            while (indent > 0)
                            {
                                opcode = SkipNext(ref stream);
                                switch (opcode)
                                {
                                    case OpCode.IF: indent++; break;
                                    case OpCode.EIF: indent--; break;
                                    case OpCode.ELSE:
                                        if (indent == 1)
                                            indent = 0;
                                        break;
                                }
                            }
                        }
                    }
                    break;
                    case OpCode.ELSE:
                    {
                        // assume we hit the true statement of some previous if block
                        // if we had hit false, we would have jumped over this
                        int indent = 1;
                        while (indent > 0)
                        {
                            opcode = SkipNext(ref stream);
                            switch (opcode)
                            {
                                case OpCode.IF: indent++; break;
                                case OpCode.EIF: indent--; break;
                            }
                        }
                    }
                    break;
                    case OpCode.EIF: /* nothing to do */ break;
                    case OpCode.JROT:
                    case OpCode.JROF:
                    {
                        if (stack.PopBool() == (opcode == OpCode.JROT))
                            stream.Jump(stack.Pop() - 1);
                        else
                            stack.Pop();    // ignore the offset
                    }
                    break;
                    case OpCode.JMPR: stream.Jump(stack.Pop() - 1); break;

                    // ==== LOGICAL OPS ====
                    case OpCode.LT:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a < b);
                    }
                    break;
                    case OpCode.LTEQ:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a <= b);
                    }
                    break;
                    case OpCode.GT:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a > b);
                    }
                    break;
                    case OpCode.GTEQ:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a >= b);
                    }
                    break;
                    case OpCode.EQ:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a == b);
                    }
                    break;
                    case OpCode.NEQ:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a != b);
                    }
                    break;
                    case OpCode.AND:
                    {
                        var b = stack.PopBool();
                        var a = stack.PopBool();
                        stack.Push(a && b);
                    }
                    break;
                    case OpCode.OR:
                    {
                        var b = stack.PopBool();
                        var a = stack.PopBool();
                        stack.Push(a || b);
                    }
                    break;
                    case OpCode.NOT: stack.Push(!stack.PopBool()); break;
                    case OpCode.ODD:
                    {
                        var value = (int)Round(stack.PopFloat());
                        stack.Push(value % 2 != 0);
                    }
                    break;
                    case OpCode.EVEN:
                    {
                        var value = (int)Round(stack.PopFloat());
                        stack.Push(value % 2 == 0);
                    }
                    break;

                    // ==== ARITHMETIC ====
                    case OpCode.ADD:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a + b);
                    }
                    break;
                    case OpCode.SUB:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        stack.Push(a - b);
                    }
                    break;
                    case OpCode.DIV:
                    {
                        var b = stack.Pop();
                        if (b == 0)
                            throw new InvalidFontException("Division by zero.");

                        var a = stack.Pop();
                        var result = ((long)a << 6) / b;
                        stack.Push((int)result);
                    }
                    break;
                    case OpCode.MUL:
                    {
                        var b = stack.Pop();
                        var a = stack.Pop();
                        var result = (long)a * b >> 6;
                        stack.Push((int)result);
                    }
                    break;
                    case OpCode.ABS: stack.Push(Math.Abs(stack.Pop())); break;
                    case OpCode.NEG: stack.Push(-stack.Pop()); break;
                    case OpCode.FLOOR: stack.Push(stack.Pop() & ~63); break;
                    case OpCode.CEILING: stack.Push(stack.Pop() + 63 & ~63); break;
                    case OpCode.MAX: stack.Push(Math.Max(stack.Pop(), stack.Pop())); break;
                    case OpCode.MIN: stack.Push(Math.Min(stack.Pop(), stack.Pop())); break;

                    // ==== FUNCTIONS ====
                    case OpCode.FDEF:
                    {
                        if (!allowFunctionDefs || inFunction)
                            throw new InvalidFontException("Can't define functions here.");

                        functions[stack.Pop()] = stream;
                        while (SkipNext(ref stream) != OpCode.ENDF) ;
                    }
                    break;
                    case OpCode.IDEF:
                    {
                        if (!allowFunctionDefs || inFunction)
                            throw new InvalidFontException("Can't define functions here.");

                        instructionDefs[stack.Pop()] = stream;
                        while (SkipNext(ref stream) != OpCode.ENDF) ;
                    }
                    break;
                    case OpCode.ENDF:
                    {
                        if (!inFunction)
                            throw new InvalidFontException("Found invalid ENDF marker outside of a function definition.");
                        return;
                    }
                    case OpCode.CALL:
                    case OpCode.LOOPCALL:
                    {
                        callStackSize++;
                        if (callStackSize > MaxCallStack)
                            throw new InvalidFontException("Stack overflow; infinite recursion?");

                        var function = functions[stack.Pop()];
                        var count = opcode == OpCode.LOOPCALL ? stack.Pop() : 1;
                        for (int i = 0; i < count; i++)
                            Execute(function, true, false);
                        callStackSize--;
                    }
                    break;

                    // ==== ROUNDING ====
                    // we don't have "engine compensation" so the variants are unnecessary
                    case OpCode.ROUND0:
                    case OpCode.ROUND1:
                    case OpCode.ROUND2:
                    case OpCode.ROUND3: stack.Push(Round(stack.PopFloat())); break;
                    case OpCode.NROUND0:
                    case OpCode.NROUND1:
                    case OpCode.NROUND2:
                    case OpCode.NROUND3: break;

                    // ==== DELTA EXCEPTIONS ====
                    case OpCode.DELTAC1:
                    case OpCode.DELTAC2:
                    case OpCode.DELTAC3:
                    {
                        var last = stack.Pop();
                        for (int i = 1; i <= last; i++)
                        {
                            var cvtIndex = stack.Pop();
                            var arg = stack.Pop();

                            // upper 4 bits of the 8-bit arg is the relative ppem
                            // the opcode specifies the base to add to the ppem
                            var triggerPpem = arg >> 4 & 0xF;
                            triggerPpem += (opcode - OpCode.DELTAC1) * 16;
                            triggerPpem += state.DeltaBase;

                            // if the current ppem matches the trigger, apply the exception
                            if (ppem == triggerPpem)
                            {
                                // the lower 4 bits of the arg is the amount to shift
                                // it's encoded such that 0 isn't an allowable value (who wants to shift by 0 anyway?)
                                var amount = (arg & 0xF) - 8;
                                if (amount >= 0)
                                    amount++;
                                amount *= 1 << 6 - state.DeltaShift;

                                // update the CVT
                                CheckIndex(cvtIndex, controlValueTable.Length);
                                controlValueTable[cvtIndex] += F26Dot6ToFloat(amount);
                            }
                        }
                    }
                    break;
                    case OpCode.DELTAP1:
                    case OpCode.DELTAP2:
                    case OpCode.DELTAP3:
                    {
                        var last = stack.Pop();
                        for (int i = 1; i <= last; i++)
                        {
                            var pointIndex = stack.Pop();
                            var arg = stack.Pop();

                            // upper 4 bits of the 8-bit arg is the relative ppem
                            // the opcode specifies the base to add to the ppem
                            var triggerPpem = arg >> 4 & 0xF;
                            triggerPpem += state.DeltaBase;
                            if (opcode != OpCode.DELTAP1)
                                triggerPpem += (opcode - OpCode.DELTAP2 + 1) * 16;

                            // if the current ppem matches the trigger, apply the exception
                            if (ppem == triggerPpem)
                            {
                                // the lower 4 bits of the arg is the amount to shift
                                // it's encoded such that 0 isn't an allowable value (who wants to shift by 0 anyway?)
                                var amount = (arg & 0xF) - 8;
                                if (amount >= 0)
                                    amount++;
                                amount *= 1 << 6 - state.DeltaShift;

                                MovePoint(zp0, pointIndex, F26Dot6ToFloat(amount));
                            }
                        }
                    }
                    break;

                    // ==== MISCELLANEOUS ====
                    case OpCode.DEBUG: stack.Pop(); break;
                    case OpCode.GETINFO:
                    {
                        var selector = stack.Pop();
                        var result = 0;
                        if ((selector & 0x1) != 0)
                        {
                            // pretend we are MS Rasterizer v35
                            result = 35;
                        }

                        // TODO: rotation and stretching
                        //if ((selector & 0x2) != 0)
                        //if ((selector & 0x4) != 0)

                        // we're always rendering in grayscale
                        if ((selector & 0x20) != 0)
                            result |= 1 << 12;

                        // TODO: ClearType flags

                        stack.Push(result);
                    }
                    break;

                    default:
                        if (opcode >= OpCode.MIRP)
                            MoveIndirectRelative(opcode - OpCode.MIRP);
                        else if (opcode >= OpCode.MDRP)
                            MoveDirectRelative(opcode - OpCode.MDRP);
                        else
                        {
                            // check if this is a runtime-defined opcode
                            var index = (int)opcode;
                            if (index > instructionDefs.Length || !instructionDefs[index].IsValid)
                                throw new InvalidFontException("Unknown opcode in font program.");

                            callStackSize++;
                            if (callStackSize > MaxCallStack)
                                throw new InvalidFontException("Stack overflow; infinite recursion?");

                            Execute(instructionDefs[index], true, false);
                            callStackSize--;
                        }
                        break;
                }
            }
        }

        int CheckIndex(int index, int length)
        {
            if (index < 0 || index >= length)
                throw new InvalidFontException();
            return index;
        }

        float ReadCvt() => controlValueTable[CheckIndex(stack.Pop(), controlValueTable.Length)];

        void OnVectorsUpdated()
        {
            fdotp = Vector2.Dot(state.Freedom, state.Projection);
            if (Math.Abs(fdotp) < Epsilon)
                fdotp = 1.0f;
        }

        void SetFreedomVectorToAxis(int axis)
        {
            state.Freedom = axis == 0 ? Vector2.UnitY : Vector2.UnitX;
            OnVectorsUpdated();
        }

        void SetProjectionVectorToAxis(int axis)
        {
            state.Projection = axis == 0 ? Vector2.UnitY : Vector2.UnitX;
            state.DualProjection = state.Projection;

            OnVectorsUpdated();
        }

        void SetVectorToLine(int mode, bool dual)
        {
            // mode here should be as follows:
            // 0: SPVTL0
            // 1: SPVTL1
            // 2: SFVTL0
            // 3: SFVTL1
            var index1 = stack.Pop();
            var index2 = stack.Pop();
            var p1 = zp2.GetCurrent(index1);
            var p2 = zp1.GetCurrent(index2);

            var line = p2 - p1;
            if (line.LengthSquared() == 0)
            {
                // invalid; just set to whatever
                if (mode >= 2)
                    state.Freedom = Vector2.UnitX;
                else
                {
                    state.Projection = Vector2.UnitX;
                    state.DualProjection = Vector2.UnitX;
                }
            }
            else
            {
                // if mode is 1 or 3, we want a perpendicular vector
                if ((mode & 0x1) != 0)
                    line = new Vector2(-line.Y, line.X);
                line = Vector2.Normalize(line);

                if (mode >= 2)
                    state.Freedom = line;
                else
                {
                    state.Projection = line;
                    state.DualProjection = line;
                }
            }

            // set the dual projection vector using original points
            if (dual)
            {
                p1 = zp2.GetOriginal(index1);
                p2 = zp2.GetOriginal(index2);
                line = p2 - p1;

                if (line.LengthSquared() == 0)
                    state.DualProjection = Vector2.UnitX;
                else
                {
                    if ((mode & 0x1) != 0)
                        line = new Vector2(-line.Y, line.X);

                    state.DualProjection = Vector2.Normalize(line);
                }
            }

            OnVectorsUpdated();
        }

        Zone GetZoneFromStack()
        {
            switch (stack.Pop())
            {
                case 0: return twilight;
                case 1: return points;
                default: throw new InvalidFontException("Invalid zone pointer.");
            }
        }

        void SetSuperRound(float period)
        {
            // mode is a bunch of packed flags
            // bits 7-6 are the period multiplier
            var mode = stack.Pop();
            switch (mode & 0xC0)
            {
                case 0: roundPeriod = period / 2; break;
                case 0x40: roundPeriod = period; break;
                case 0x80: roundPeriod = period * 2; break;
                default: throw new InvalidFontException("Unknown rounding period multiplier.");
            }

            // bits 5-4 are the phase
            switch (mode & 0x30)
            {
                case 0: roundPhase = 0; break;
                case 0x10: roundPhase = roundPeriod / 4; break;
                case 0x20: roundPhase = roundPeriod / 2; break;
                case 0x30: roundPhase = roundPeriod * 3 / 4; break;
            }

            // bits 3-0 are the threshold
            if ((mode & 0xF) == 0)
                roundThreshold = roundPeriod - 1;
            else
                roundThreshold = ((mode & 0xF) - 4) * roundPeriod / 8;
        }

        void MoveIndirectRelative(int flags)
        {
            // this instruction tries to make the current distance between a given point
            // and the reference point rp0 be equivalent to the same distance in the original outline
            // there are a bunch of flags that control how that distance is measured
            var cvt = ReadCvt();
            var pointIndex = stack.Pop();

            if (Math.Abs(cvt - state.SingleWidthValue) < state.SingleWidthCutIn)
            {
                if (cvt >= 0)
                    cvt = state.SingleWidthValue;
                else
                    cvt = -state.SingleWidthValue;
            }

            // if we're looking at the twilight zone we need to prepare the points there
            var originalReference = zp0.GetOriginal(state.Rp0);
            if (zp1.IsTwilight)
            {
                var initialValue = originalReference + state.Freedom * cvt;
                zp1.Original[pointIndex].P = initialValue;
                zp1.Current[pointIndex].P = initialValue;
            }

            var point = zp1.GetCurrent(pointIndex);
            var originalDistance = DualProject(zp1.GetOriginal(pointIndex) - originalReference);
            var currentDistance = Project(point - zp0.GetCurrent(state.Rp0));

            if (state.AutoFlip && Math.Sign(originalDistance) != Math.Sign(cvt))
                cvt = -cvt;

            // if bit 2 is set, round the distance and look at the cut-in value
            var distance = cvt;
            if ((flags & 0x4) != 0)
            {
                // only perform cut-in tests when both points are in the same zone
                if (zp0.IsTwilight == zp1.IsTwilight && Math.Abs(cvt - originalDistance) > state.ControlValueCutIn)
                    cvt = originalDistance;
                distance = Round(cvt);
            }

            // if bit 3 is set, constrain to the minimum distance
            if ((flags & 0x8) != 0)
            {
                if (originalDistance >= 0)
                    distance = Math.Max(distance, state.MinDistance);
                else
                    distance = Math.Min(distance, -state.MinDistance);
            }

            // move the point
            MovePoint(zp1, pointIndex, distance - currentDistance);
            state.Rp1 = state.Rp0;
            state.Rp2 = pointIndex;
            if ((flags & 0x10) != 0)
                state.Rp0 = pointIndex;
        }

        void MoveDirectRelative(int flags)
        {
            // determine the original distance between the two reference points
            var pointIndex = stack.Pop();
            var p1 = zp0.GetOriginal(state.Rp0);
            var p2 = zp1.GetOriginal(pointIndex);
            var originalDistance = DualProject(p2 - p1);

            // single width cutin test
            if (Math.Abs(originalDistance - state.SingleWidthValue) < state.SingleWidthCutIn)
            {
                if (originalDistance >= 0)
                    originalDistance = state.SingleWidthValue;
                else
                    originalDistance = -state.SingleWidthValue;
            }

            // if bit 2 is set, perform rounding
            var distance = originalDistance;
            if ((flags & 0x4) != 0)
                distance = Round(distance);

            // if bit 3 is set, constrain to the minimum distance
            if ((flags & 0x8) != 0)
            {
                if (originalDistance >= 0)
                    distance = Math.Max(distance, state.MinDistance);
                else
                    distance = Math.Min(distance, -state.MinDistance);
            }

            // move the point
            originalDistance = Project(zp1.GetCurrent(pointIndex) - zp0.GetCurrent(state.Rp0));
            MovePoint(zp1, pointIndex, distance - originalDistance);
            state.Rp1 = state.Rp0;
            state.Rp2 = pointIndex;
            if ((flags & 0x10) != 0)
                state.Rp0 = pointIndex;
        }

        Vector2 ComputeDisplacement(int mode, out Zone zone, out int point)
        {
            // compute displacement of the reference point
            if ((mode & 1) == 0)
            {
                zone = zp1;
                point = state.Rp2;
            }
            else
            {
                zone = zp0;
                point = state.Rp1;
            }

            var distance = Project(zone.GetCurrent(point) - zone.GetOriginal(point));
            return distance * state.Freedom / fdotp;
        }

        TouchState GetTouchState()
        {
            var touch = TouchState.None;
            if (state.Freedom.X != 0)
                touch = TouchState.X;
            if (state.Freedom.Y != 0)
                touch |= TouchState.Y;

            return touch;
        }

        void ShiftPoints(Vector2 displacement)
        {
            var touch = GetTouchState();
            for (int i = 0; i < state.Loop; i++)
            {
                var pointIndex = stack.Pop();
                zp2.Current[pointIndex].P += displacement;
                zp2.TouchState[pointIndex] |= touch;
            }
            state.Loop = 1;
        }

        void MovePoint(Zone zone, int index, float distance)
        {
            var point = zone.GetCurrent(index) + distance * state.Freedom / fdotp;
            var touch = GetTouchState();
            zone.Current[index].P = point;
            zone.TouchState[index] |= touch;
        }

        float Round(float value)
        {
            switch (state.RoundState)
            {
                case RoundMode.ToGrid: return value >= 0 ? (float)Math.Round(value) : -(float)Math.Round(-value);
                case RoundMode.ToHalfGrid: return value >= 0 ? (float)Math.Floor(value) + 0.5f : -((float)Math.Floor(-value) + 0.5f);
                case RoundMode.ToDoubleGrid: return value >= 0 ? (float)(Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2) : -(float)(Math.Round(-value * 2, MidpointRounding.AwayFromZero) / 2);
                case RoundMode.DownToGrid: return value >= 0 ? (float)Math.Floor(value) : -(float)Math.Floor(-value);
                case RoundMode.UpToGrid: return value >= 0 ? (float)Math.Ceiling(value) : -(float)Math.Ceiling(-value);
                case RoundMode.Super:
                case RoundMode.Super45:
                    float result;
                    if (value >= 0)
                    {
                        result = value - roundPhase + roundThreshold;
                        result = (float)Math.Truncate(result / roundPeriod) * roundPeriod;
                        result += roundPhase;
                        if (result < 0)
                            result = roundPhase;
                    }
                    else
                    {
                        result = -value - roundPhase + roundThreshold;
                        result = -(float)Math.Truncate(result / roundPeriod) * roundPeriod;
                        result -= roundPhase;
                        if (result > 0)
                            result = -roundPhase;
                    }
                    return result;

                default: return value;
            }
        }

        float Project(Vector2 point) => Vector2.Dot(point, state.Projection);
        float DualProject(Vector2 point) => Vector2.Dot(point, state.DualProjection);

        static OpCode SkipNext(ref InstructionStream stream)
        {
            // grab the next opcode, and if it's one of the push instructions skip over its arguments
            var opcode = stream.NextOpCode();
            switch (opcode)
            {
                case OpCode.NPUSHB:
                case OpCode.PUSHB1:
                case OpCode.PUSHB2:
                case OpCode.PUSHB3:
                case OpCode.PUSHB4:
                case OpCode.PUSHB5:
                case OpCode.PUSHB6:
                case OpCode.PUSHB7:
                case OpCode.PUSHB8:
                {
                    var count = opcode == OpCode.NPUSHB ? stream.NextByte() : opcode - OpCode.PUSHB1 + 1;
                    for (int i = 0; i < count; i++)
                        stream.NextByte();
                }
                break;
                case OpCode.NPUSHW:
                case OpCode.PUSHW1:
                case OpCode.PUSHW2:
                case OpCode.PUSHW3:
                case OpCode.PUSHW4:
                case OpCode.PUSHW5:
                case OpCode.PUSHW6:
                case OpCode.PUSHW7:
                case OpCode.PUSHW8:
                {
                    var count = opcode == OpCode.NPUSHW ? stream.NextByte() : opcode - OpCode.PUSHW1 + 1;
                    for (int i = 0; i < count; i++)
                        stream.NextWord();
                }
                break;
            }

            return opcode;
        }

        static unsafe void InterpolatePoints(byte* current, byte* original, int start, int end, int ref1, int ref2)
        {
            if (start > end)
                return;

            // figure out how much the two reference points
            // have been shifted from their original positions
            float delta1, delta2;
            var lower = *GetPoint(original, ref1);
            var upper = *GetPoint(original, ref2);
            if (lower > upper)
            {
                var temp = lower;
                lower = upper;
                upper = temp;

                delta1 = *GetPoint(current, ref2) - lower;
                delta2 = *GetPoint(current, ref1) - upper;
            }
            else
            {
                delta1 = *GetPoint(current, ref1) - lower;
                delta2 = *GetPoint(current, ref2) - upper;
            }

            var lowerCurrent = delta1 + lower;
            var upperCurrent = delta2 + upper;
            var scale = (upperCurrent - lowerCurrent) / (upper - lower);

            for (int i = start; i <= end; i++)
            {
                // three cases: if it's to the left of the lower reference point or to
                // the right of the upper reference point, do a shift based on that ref point.
                // otherwise, interpolate between the two of them
                var pos = *GetPoint(original, i);
                if (pos <= lower)
                    pos += delta1;
                else if (pos >= upper)
                    pos += delta2;
                else
                    pos = lowerCurrent + (pos - lower) * scale;
                *GetPoint(current, i) = pos;
            }
        }

        static float F2Dot14ToFloat(int value) => (short)value / 16384.0f;
        static int FloatToF2Dot14(float value) => (int)(uint)(short)Math.Round(value * 16384.0f);
        static float F26Dot6ToFloat(int value) => value / 64.0f;
        static int FloatToF26Dot6(float value) => (int)Math.Round(value * 64.0f);

        unsafe static float* GetPoint(byte* data, int index) => (float*)(data + sizeof(PointF) * index);

        static readonly float Sqrt2Over2 = (float)(Math.Sqrt(2) / 2);

        const int MaxCallStack = 128;
        const float Epsilon = 0.000001f;

        struct InstructionStream
        {
            byte[] instructions;
            int ip;

            public bool IsValid => instructions != null;
            public bool Done => ip >= instructions.Length;

            public InstructionStream(byte[] instructions)
            {
                this.instructions = instructions;
                ip = 0;
            }

            public int NextByte()
            {
                if (Done)
                    throw new InvalidFontException();
                return instructions[ip++];
            }

            public OpCode NextOpCode() => (OpCode)NextByte();
            public int NextWord() => (short)(ushort)(NextByte() << 8 | NextByte());
            public void Jump(int offset) => ip += offset;
        }

        struct GraphicsState
        {
            public Vector2 Freedom;
            public Vector2 DualProjection;
            public Vector2 Projection;
            public InstructionControlFlags InstructionControl;
            public RoundMode RoundState;
            public float MinDistance;
            public float ControlValueCutIn;
            public float SingleWidthCutIn;
            public float SingleWidthValue;
            public int DeltaBase;
            public int DeltaShift;
            public int Loop;
            public int Rp0;
            public int Rp1;
            public int Rp2;
            public bool AutoFlip;

            public void Reset()
            {
                Freedom = Vector2.UnitX;
                Projection = Vector2.UnitX;
                DualProjection = Vector2.UnitX;
                InstructionControl = InstructionControlFlags.None;
                RoundState = RoundMode.ToGrid;
                MinDistance = 1.0f;
                ControlValueCutIn = 17.0f / 16.0f;
                SingleWidthCutIn = 0.0f;
                SingleWidthValue = 0.0f;
                DeltaBase = 9;
                DeltaShift = 3;
                Loop = 1;
                Rp0 = Rp1 = Rp2 = 0;
                AutoFlip = true;
            }
        }

        class ExecutionStack
        {
            int[] s;
            int count;

            public ExecutionStack(int maxStack)
            {
                s = new int[maxStack];
            }

            public int Peek() => Peek(0);
            public bool PopBool() => Pop() != 0;
            public float PopFloat() => F26Dot6ToFloat(Pop());
            public void Push(bool value) => Push(value ? 1 : 0);
            public void Push(float value) => Push(FloatToF26Dot6(value));

            public void Clear() => count = 0;
            public void Depth() => Push(count);
            public void Duplicate() => Push(Peek());
            public void Copy() => Copy(Pop() - 1);
            public void Copy(int index) => Push(Peek(index));
            public void Move() => Move(Pop() - 1);
            public void Roll() => Move(2);

            public void Move(int index)
            {
                var val = Peek(index);
                for (int i = count - index - 1; i < count - 1; i++)
                    s[i] = s[i + 1];
                s[count - 1] = val;
            }

            public void Swap()
            {
                if (count < 2)
                    throw new InvalidFontException();

                var tmp = s[count - 1];
                s[count - 1] = s[count - 2];
                s[count - 2] = tmp;
            }

            public void Push(int value)
            {
                if (count == s.Length)
                    throw new InvalidFontException();
                s[count++] = value;
            }

            public int Pop()
            {
                if (count == 0)
                    throw new InvalidFontException();
                return s[--count];
            }

            public int Peek(int index)
            {
                if (index < 0 || index >= count)
                    throw new InvalidFontException();
                return s[count - index - 1];
            }
        }

        struct Zone
        {
            public PointF[] Current;
            public PointF[] Original;
            public TouchState[] TouchState;
            public bool IsTwilight;

            public Zone(PointF[] points, bool isTwilight)
            {
                IsTwilight = isTwilight;
                Current = points;
                Original = (PointF[])points.Clone();
                TouchState = new TouchState[points.Length];
            }

            public Vector2 GetCurrent(int index) => Current[index].P;
            public Vector2 GetOriginal(int index) => Original[index].P;
        }

        enum RoundMode
        {
            ToHalfGrid,
            ToGrid,
            ToDoubleGrid,
            DownToGrid,
            UpToGrid,
            Off,
            Super,
            Super45
        }

        [Flags]
        enum InstructionControlFlags
        {
            None,
            InhibitGridFitting = 0x1,
            UseDefaultGraphicsState = 0x2
        }

        [Flags]
        enum TouchState
        {
            None = 0,
            X = 0x1,
            Y = 0x2,
            Both = X | Y
        }

        enum OpCode : byte
        {
            SVTCA0,
            SVTCA1,
            SPVTCA0,
            SPVTCA1,
            SFVTCA0,
            SFVTCA1,
            SPVTL0,
            SPVTL1,
            SFVTL0,
            SFVTL1,
            SPVFS,
            SFVFS,
            GPV,
            GFV,
            SFVTPV,
            ISECT,
            SRP0,
            SRP1,
            SRP2,
            SZP0,
            SZP1,
            SZP2,
            SZPS,
            SLOOP,
            RTG,
            RTHG,
            SMD,
            ELSE,
            JMPR,
            SCVTCI,
            SSWCI,
            SSW,
            DUP,
            POP,
            CLEAR,
            SWAP,
            DEPTH,
            CINDEX,
            MINDEX,
            ALIGNPTS,
            /* unused: 0x28 */
            UTP = 0x29,
            LOOPCALL,
            CALL,
            FDEF,
            ENDF,
            MDAP0,
            MDAP1,
            IUP0,
            IUP1,
            SHP0,
            SHP1,
            SHC0,
            SHC1,
            SHZ0,
            SHZ1,
            SHPIX,
            IP,
            MSIRP0,
            MSIRP1,
            ALIGNRP,
            RTDG,
            MIAP0,
            MIAP1,
            NPUSHB,
            NPUSHW,
            WS,
            RS,
            WCVTP,
            RCVT,
            GC0,
            GC1,
            SCFS,
            MD0,
            MD1,
            MPPEM,
            MPS,
            FLIPON,
            FLIPOFF,
            DEBUG,
            LT,
            LTEQ,
            GT,
            GTEQ,
            EQ,
            NEQ,
            ODD,
            EVEN,
            IF,
            EIF,
            AND,
            OR,
            NOT,
            DELTAP1,
            SDB,
            SDS,
            ADD,
            SUB,
            DIV,
            MUL,
            ABS,
            NEG,
            FLOOR,
            CEILING,
            ROUND0,
            ROUND1,
            ROUND2,
            ROUND3,
            NROUND0,
            NROUND1,
            NROUND2,
            NROUND3,
            WCVTF,
            DELTAP2,
            DELTAP3,
            DELTAC1,
            DELTAC2,
            DELTAC3,
            SROUND,
            S45ROUND,
            JROT,
            JROF,
            ROFF,
            /* unused: 0x7B */
            RUTG = 0x7C,
            RDTG,
            SANGW,
            AA,
            FLIPPT,
            FLIPRGON,
            FLIPRGOFF,
            /* unused: 0x83 - 0x84 */
            SCANCTRL = 0x85,
            SDPVTL0,
            SDPVTL1,
            GETINFO,
            IDEF,
            ROLL,
            MAX,
            MIN,
            SCANTYPE,
            INSTCTRL,
            /* unused: 0x8F - 0xAF */
            PUSHB1 = 0xB0,
            PUSHB2,
            PUSHB3,
            PUSHB4,
            PUSHB5,
            PUSHB6,
            PUSHB7,
            PUSHB8,
            PUSHW1,
            PUSHW2,
            PUSHW3,
            PUSHW4,
            PUSHW5,
            PUSHW6,
            PUSHW7,
            PUSHW8,
            MDRP,           // range of 32 values, 0xC0 - 0xDF,
            MIRP = 0xE0     // range of 32 values, 0xE0 - 0xFF
        }
    }
}
