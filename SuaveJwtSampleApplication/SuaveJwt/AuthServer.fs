﻿module AuthServer

open Suave.Http
open JwtToken
open System
open SuaveJson
open Suave.RequestErrors
open Suave
open Suave.Filters
open Suave.Operators

type AudienceCreateRequest = {
    Name : string
}

type AudienceCreateResponse = {
    ClientId : string
    Base64Secret : string
    Name : string
}

type TokenCreateCredentials = {
    UserName : string
    Password : string
    ClientId : string
}

type Config = {
    AddAudienceUrlPath : string
    CreateTokenUrlPath : string
    SaveAudience : Audience -> Async<Audience>
    GetAudience : string -> Async<Audience option>
    Issuer : string
    TokenTimeSpan : TimeSpan
}

let audienceWebPart config identityStore =

    let toAudienceCreateResponse (audience : Audience) = {
        Base64Secret = audience.Secret.ToString()
        ClientId = audience.ClientId        
        Name = audience.Name
    }

    let tryCreateAudience (ctx: HttpContext) =
        match mapJsonPayload<AudienceCreateRequest> ctx.request with
        | Some audienceCreateRequest -> 
            async {
                let! audience = audienceCreateRequest.Name |> createAudience |> config.SaveAudience                     
                let audienceCreateResponse = toAudienceCreateResponse audience
                return! JSON audienceCreateResponse ctx
            }
        | None -> BAD_REQUEST "Invalid Audience Create Request" ctx

    let tryCreateToken (ctx: HttpContext) =
        match mapJsonPayload<TokenCreateCredentials> ctx.request with
        | Some tokenCreateCredentials -> 
            async {
                let! audience = config.GetAudience tokenCreateCredentials.ClientId
                match audience with
                | Some audience ->
                    let tokenCreateRequest : TokenCreateRequest = {         
                        Issuer = config.Issuer        
                        UserName = tokenCreateCredentials.UserName
                        Password = tokenCreateCredentials.Password        
                        TokenTimeSpan = config.TokenTimeSpan
                    }
                    
                    let! token = createToken tokenCreateRequest identityStore audience
                    match token with
                    | Some token -> return! JSON token ctx
                    | None -> return! BAD_REQUEST "Invalid Login Credentials" ctx
                    
                | None -> return! BAD_REQUEST "Invalid Client Id" ctx
            }
        
        | None -> BAD_REQUEST "Invalid Token Create Request" ctx

    choose [
        path config.AddAudienceUrlPath >=> POST >=> tryCreateAudience
        path config.CreateTokenUrlPath >=> POST >=> tryCreateToken
    ]