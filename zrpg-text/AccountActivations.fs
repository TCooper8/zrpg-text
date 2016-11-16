namespace Zrpg

open System

module AccountActivations =
  type AccountActivation = {
    id: Guid
    emailAddress: string
    screenName: string
  }

  type PostAccountActivation = {
    emailAddress: string
    screenName: string
  }

  type PostAccountActivationRequest = {
    token: string
    cmd: PostAccountActivation
  }

  type PostAccountActivationResult =
    | PostAccountActivationCreated of PostAccountActivationCreated
    | PostAccountActivationFailure of PostAccountActivationFailure

  and PostAccountActivationCreated = {
    id: Guid
  }

  and PostAccountActivationFailure = {
    reason: string
  }

  [<Interface>]
  type AccountActivations =
    abstract member post: PostAccountActivationRequest -> PostAccountActivationResult