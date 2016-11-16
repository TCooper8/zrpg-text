namespace Zrpg

open System

open Zrpg.Heroes

module Kingdoms =
  type Kingdom = {
    id: Guid
    screenName: string
    heroes: Hero seq
  }

  type GetKingdom = {
    id: Guid 
  }

  type GetKingdomRequest = {
    token: string
    cmd: GetKingdom
  }

  type GetKingdomResult =
    | GetKingdomOk of GetKingdomOk
    | GetKingdomNotFound of GetKingdomNotFound

  and GetKingdomOk = {
    kingdom: Kingdom
  }

  and GetKingdomNotFound = {
    reason: string
  }

  type PostKingdom = {
    screenName: string
  }

  type PostKingdomResult =
    | PostKingdomCreated of PostKingdomCreated
    | PostKingdomConflict of PostKingdomConflict

  and PostKingdomCreated = {
    id: Guid
  }

  and PostKingdomConflict = {
    reason: string
  }

  [<Interface>]
  type Kingdoms =
    abstract member get: GetKingdomRequest -> GetKingdomResult
    abstract member post: PostKingdom -> PostKingdomResult