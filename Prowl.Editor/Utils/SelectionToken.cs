// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System;


/// <summary>
/// A selection entry stored as identifiers only, so it never pins the script ALC.
/// </summary>
public readonly record struct SelectionToken(SelKind Kind, Guid Id, Guid CompId, string Path, string Name, bool IsFolder);

public enum SelKind { GameObject, Component, Asset, Content }
