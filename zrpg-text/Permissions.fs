namespace Zrpg

open System

module Permissions =
  type Permission = {
    granterId: Guid
    method: string
    uri: string
    authId: Guid
  }

  type PostPermission = {
    granterToken: Guid
    method: string
    uri: string
    token: Guid
  }

  type PostPermissionResult =
    | PostPermissionOk of PostPermissionOk

  and PostPermissionOk = {
    status: string
  }

  [<Interface>]
  type Permissions =
    abstract member post: PostPermission -> PostPermissionResult