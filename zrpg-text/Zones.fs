namespace Zrpg

open System

module Zones =
  type Zone = {
    id: Guid
    regionId: Guid
    name: string
  }

  type PostZone = {
    regionId: Guid
    name: string
  }

  type PostZoneRequest = {
    token: string
    cmd: PostZone
  }

  type PostZoneResult =
    | PostZoneCreated of PostZoneCreated
    | PostZoneConflict of PostZoneConflict
    | PostZoneForbidden of PostZoneForbidden

  and PostZoneCreated = {
    id: Guid
  }

  and PostZoneConflict = {
    reason: string 
  }

  and PostZoneForbidden = {
    reason: string
  }

  type PutZone = {
    id: Guid
    regionId: Guid
    name: string
  }

  type PutZoneRequest = {
    token: string
    cmd: PutZone
  }

  type PutZoneResult =
    | PutZoneOk of PutZoneOk
    | PutZoneConflict of PutZoneConflict

  and PutZoneOk = {
    status: string
  }

  and PutZoneConflict = {
    reason: string
  }

  [<Interface>]
  type Zones =
    abstract member post: PostZoneRequest -> PostZoneResult
    abstract member put: PutZoneRequest -> PutZoneResult