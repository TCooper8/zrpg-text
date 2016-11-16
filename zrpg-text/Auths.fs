namespace Zrpg

open System
open Zrpg.Accounts

module Auths =
  type Auth = {
    id: Guid
    login: string
    passwordHash: string
  }

  type GetAuth = {
    login: string
    password: string
  }

  type GetAuthResult =
    | GetAuthOk of GetAuthOk
    | GetAuthUnauthorized of GetAuthUnauthorized
    | GetAuthFailure of GetAuthFailure

  and GetAuthOk = {
    token: string
    account: Account
  }

  and GetAuthUnauthorized = {
    reason: string
  }

  and GetAuthFailure = {
    reason: string
  }

  type PostAuth = {
    login: string
    passwordHash: string
  }

  type PostAuthResult =
    | PostAuthCreated of PostAuthCreated
    | PostAuthConflict of PostAuthConflict

  and PostAuthCreated = {
    id: Guid
  }

  and PostAuthConflict = {
    reason: string
  }

  [<Interface>]
  type Auths =
    abstract member get: GetAuth -> GetAuthResult
    abstract member post: PostAuth -> PostAuthResult