namespace Zrpg

open System

module Utils =
  let join separator (lines: string seq) =
    String.Join(separator, lines)