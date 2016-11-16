namespace Zrpg

open System

module Accounts =
  type Account = {
    id: Guid
    authId: Guid
    kingdomId: Guid
    emailAddress: string
  }

  type PostAccount = {
    activationId: Guid
    password: string
  }

  type PostAccountResult =
    | PostAccountCreated of PostAccountCreated
    | PostAccountFailure of PostAccountFailure

  and PostAccountCreated = {
    id: Guid
  }

  and PostAccountFailure = {
    reason: string
  }

  [<Interface>]
  type Accounts =
    abstract member post: PostAccount -> PostAccountResult