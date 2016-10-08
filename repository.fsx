#r "packages/octokit/lib/net45/octokit.dll"
#r "system.net.http"
open System
open System.IO
open System.Reflection
open System.Threading
open System.Net.Http
open Octokit
open Octokit.Internal

module Input =
    // tweaked from UserInputHelper.fs in FAKE - https://github.com/fsharp/FAKE/blob/master/src/app/FakeLib/UserInputHelper.fs
    let internal erasePreviousChar () =
        try
            let left = if Console.CursorLeft <> 0 then Console.CursorLeft-1 else Console.BufferWidth-1
            let top  = if Console.CursorLeft <> 0 then Console.CursorTop    else Console.CursorTop-1
            Console.SetCursorPosition (left, top)
            Console.Write ' '
            Console.SetCursorPosition (left, top)    
        with // Console is dumb, might be redirected. We don't care, if it isn't a screen the visual feedback isn't required
        | :? IO.IOException -> ()

    let internal readString (echo: bool) : string =
        let rec loop cs =
            let key = Console.ReadKey true
            match key.Key, cs with
            | ConsoleKey.Backspace, [] -> loop []
            | ConsoleKey.Backspace, _::cs -> erasePreviousChar (); loop cs
            | ConsoleKey.Enter, _ -> cs
            | _ ->  if echo then Console.Write key.KeyChar else Console.Write '*'
                    loop (key.KeyChar::cs)    
        loop [] |> List.rev |> Array.ofList |> fun cs -> String cs

    let internal color (color: ConsoleColor) (code : unit -> _) =
        let before = Console.ForegroundColor
        try     Console.ForegroundColor <- color; code ()
        finally Console.ForegroundColor <- before
        
    /// Return a string entered by the user followed by enter. The input is echoed to the screen.
    let getUserInput prompt =
        color ConsoleColor.White (fun _ -> printf "%s" prompt)
        let s = readString true 
        printfn "" 
        s

    /// Return a string entered by the user followed by enter. The input is replaced by '*' on the screen.
    let getUserPassword prompt =
        color ConsoleColor.White (fun _ -> printf "%s" prompt)
        let s = readString false 
        printfn ""
        s
        
module Github =
    open Input
    // Lifted from FAKE's Octokit Script - https://github.com/fsharp/FAKE/blob/master/modules/Octokit/Octokit.fsx

    // wrapper re-implementation of HttpClientAdapter which works around
    // known Octokit bug in which user-supplied timeouts are not passed to HttpClient object
    // https://github.com/octokit/octokit.net/issues/963
    type private HttpClientWithTimeout(timeout : TimeSpan) as this =
        inherit HttpClientAdapter(fun () -> HttpMessageHandlerFactory.CreateDefault())
        let setter = lazy(
            match typeof<HttpClientAdapter>.GetField("_http", BindingFlags.NonPublic ||| BindingFlags.Instance) with
            | null -> ()
            | f ->
                match f.GetValue this with
                | :? HttpClient as http -> http.Timeout <- timeout
                | _ -> ())

        interface IHttpClient with
            member __.Send(request : IRequest, ct : CancellationToken) =
                setter.Force ()
                match request with :? Request as r -> r.Timeout <- timeout | _ -> ()
                base.Send (request, ct)
    
    let createClient user password = async {
        let httpClient = new HttpClientWithTimeout (TimeSpan.FromMinutes 20.)
        let connection = Connection (ProductHeaderValue "fsharp-lang", httpClient)
        let github = GitHubClient connection
        github.Credentials <- Credentials (user, password)
        return github
    }

    let createClientWithToken token = async {        
        let httpClient = new HttpClientWithTimeout (TimeSpan.FromMinutes 20.)
        let connection = Connection (ProductHeaderValue "fsharp-lang", httpClient)
        let github = GitHubClient connection
        return github
    }        

open Input; open Github
    
let GithubEngage () = async {  
    let! client = 
        let user = getUserInput "Github Username: "
        let password = getUserPassword "Github Password: "
        createClient user password
    
    let! user = client.User.Current() |> Async.AwaitTask
    printfn "The Current User Is - %s" user.Name
} 

GithubEngage() |> Async.RunSynchronously