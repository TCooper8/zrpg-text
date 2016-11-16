namespace Zrpg

open System

open Zrpg.Zones

module Regions =
  type Region = {
    id: Guid
    name: string
    zones: Zone list
  }

  type PostRegion = {
    name: string
  }

  type PostRegionRequest = {
    token: string
    cmd: PostRegion
  }

  type PostRegionResult =
    | PostRegionCreated of PostRegionCreated
    | PostRegionConflict of PostRegionConflict

  and PostRegionCreated = {
    id: Guid
  }

  and PostRegionConflict = {
    reason: string
  }

  type PutRegion = {
    id: Guid
    name: string
  }

  type PutRegionRequest = {
    token: string
    cmd: PutRegion
  }

  type PutRegionResult =
    | PutRegionOk of PutRegionOk
    | PutRegionConflict of PutRegionConflict

  and PutRegionOk = {
    status: string
  }

  and PutRegionConflict = {
    reason: string
  }

  [<Interface>]
  type Regions =
    abstract member post: PostRegionRequest -> PostRegionResult
    abstract member put: PutRegionRequest -> PutRegionResult