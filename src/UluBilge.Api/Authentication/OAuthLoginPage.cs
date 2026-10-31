using System.Net;

namespace UluBilge.Api.Authentication
{
    internal static class OAuthLoginPage
    {
        public static string Render(
            string clientName,
            bool allowOfflineAccess,
            int refreshTokenLifetimeDays,
            string hiddenFields,
            string? error = null)
        {
            var accessDuration = allowOfflineAccess
                ? $"En fazla {refreshTokenLifetimeDays} gün boyunca otomatik yenilenebilir."
                : "Kısa süreli erişim. Otomatik yenilenmez.";
            var errorMessage = string.IsNullOrEmpty(error)
                ? ""
                : $"<div class=\"error\" id=\"login-error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</div>";

            return $$""" 
                <!doctype html>
                <html lang="tr">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>Ulu Bilge Hesap bağlantısı</title>
                    <style>
                        :root {
                            color-scheme: light;
                            font-family: Arial, "Helvetica Neue", sans-serif;
                            color: #171e43;
                            background: #2944ee;
                            font-synthesis: none;
                            --ink: #171e43;
                            --blue: #2944ee;
                            --lime: #e9ff64;
                            --pink: #ffd3e5;
                            --paper: #fffdf7;
                        }

                        * { box-sizing: border-box; }
                        body { margin: 0; }
                        h1, h2, p { margin: 0; }
                        button, input { font: inherit; }
                        svg { display: block; flex-shrink: 0; }

                        .page {
                            display: flex;
                            flex-direction: column;
                            width: 100%;
                            max-width: 1224px;
                            min-height: 100vh;
                            min-height: 100svh;
                            margin: 0 auto;
                            padding: 0 32px 32px;
                        }

                        .masthead {
                            display: flex;
                            align-items: center;
                            justify-content: space-between;
                            gap: 24px;
                            padding: 24px 0;
                            color: #fff;
                        }
                        .wordmark { font-size: 25px; font-weight: 700; letter-spacing: -1.2px; }
                        .wordmark span { color: var(--pink); }
                        .page-caption { font-size: 12px; letter-spacing: .1px; }

                        main {
                            display: grid;
                            grid-template-columns: minmax(0, 1.08fr) minmax(0, 1fr);
                            flex: 1;
                            width: 100%;
                            border: 2px solid var(--ink);
                            border-radius: 3px;
                            overflow: hidden;
                            background: var(--paper);
                        }

                        .poster {
                            position: relative;
                            min-height: 590px;
                            padding: 36px;
                            overflow: hidden;
                            border-right: 2px solid var(--ink);
                            background: var(--lime);
                        }
                        .poster-label { font-size: 13px; font-weight: 700; }
                        .poster h2 {
                            position: relative;
                            z-index: 1;
                            margin-top: 32px;
                            font-size: clamp(48px, 5.8vw, 76px);
                            font-weight: 700;
                            line-height: 1.02;
                            letter-spacing: -.065em;
                        }
                        .poster-copy {
                            position: relative;
                            z-index: 1;
                            max-width: 285px;
                            margin-top: 24px;
                            font-size: 14px;
                            line-height: 1.7;
                        }
                        .poster-art {
                            position: absolute;
                            right: 24px;
                            bottom: -40px;
                            width: 310px;
                            transform: rotate(-12deg);
                        }
                        .poster-art svg { width: 100%; height: auto; }

                        .sign-in {
                            display: flex;
                            flex-direction: column;
                            justify-content: center;
                            min-width: 0;
                            padding: 32px 36px;
                        }
                        h1 { font-size: 29px; font-weight: 700; line-height: 1.2; letter-spacing: -1px; }
                        .description { margin-top: 8px; color: #53576d; font-size: 13px; line-height: 1.6; }

                        .request {
                            margin: 22px -36px 24px;
                            padding: 15px 36px;
                            border-top: 1px solid var(--ink);
                            border-bottom: 1px solid var(--ink);
                            background: var(--pink);
                        }
                        .client-name { font-size: 14px; font-weight: 700; line-height: 1.5; overflow-wrap: anywhere; }
                        .scope { margin-top: 3px; font-size: 13px; line-height: 1.6; }
                        .duration { margin-top: 9px; color: #4b4058; font-size: 11px; line-height: 1.6; }

                        .field + .field { margin-top: 18px; }
                        label { display: block; margin-bottom: 8px; font-size: 13px; font-weight: 700; }
                        input {
                            display: block;
                            width: 100%;
                            min-height: 48px;
                            padding: 12px 14px;
                            border: 1px solid #7f8192;
                            border-radius: 2px;
                            color: var(--ink);
                            background: #fff;
                            font-size: 16px;
                        }
                        input:hover { border-color: var(--ink); }
                        input:focus-visible { border-color: var(--blue); outline: 3px solid var(--blue); outline-offset: 2px; }

                        .error {
                            margin-bottom: 22px;
                            padding: 12px 14px;
                            border: 1px solid #a0302b;
                            border-radius: 2px;
                            color: #8d2724;
                            background: #fff0e9;
                            font-size: 13px;
                            line-height: 1.6;
                            overflow-wrap: anywhere;
                        }

                        .actions {
                            display: grid;
                            grid-template-columns: minmax(0, 1fr) auto;
                            align-items: center;
                            gap: 16px;
                            margin-top: 26px;
                        }
                        button {
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            gap: 12px;
                            min-height: 50px;
                            padding: 12px 16px;
                            border: 1px solid var(--ink);
                            border-radius: 2px;
                            font-size: 13px;
                            font-weight: 700;
                            line-height: 1.5;
                            cursor: pointer;
                        }
                        button:focus-visible { outline: 3px solid var(--blue); outline-offset: 3px; }
                        .allow { justify-content: space-between; color: var(--ink); background: var(--lime); }
                        .allow:hover { background: #d9ef4e; }
                        .deny {
                            min-width: 48px;
                            padding-right: 0;
                            padding-left: 0;
                            border-color: transparent;
                            color: #464b64;
                            background: transparent;
                            font-weight: 400;
                            text-decoration: underline;
                            text-underline-offset: 4px;
                        }
                        .deny:hover { color: var(--blue); }
                        .form-note { margin-top: 18px; color: #53576d; font-size: 11px; line-height: 1.65; }

                        @media (min-width: 801px) and (max-width: 980px) {
                            .page { padding-right: 24px; padding-left: 24px; }
                            .poster { padding: 28px; }
                            .poster h2 { font-size: 54px; }
                            .poster-art { right: 0; width: 275px; }
                            .sign-in { padding-right: 28px; padding-left: 28px; }
                            .request { margin-right: -28px; margin-left: -28px; padding-right: 28px; padding-left: 28px; }
                            .actions { gap: 12px; }
                            button { gap: 8px; padding-right: 12px; padding-left: 12px; }
                        }

                        @media (max-width: 800px) {
                            .page { max-width: 660px; padding: 0 24px 24px; }
                            .masthead { padding: 22px 0; }
                            main { display: block; flex: none; }
                            .poster { min-height: 220px; padding: 28px; border-right: 0; border-bottom: 2px solid var(--ink); }
                            .poster h2 { margin-top: 20px; font-size: 50px; }
                            .poster-copy { display: none; }
                            .poster-art { right: 22px; bottom: -60px; width: 220px; }
                            .sign-in { padding: 30px 28px; }
                            .request { margin-right: -28px; margin-left: -28px; padding-right: 28px; padding-left: 28px; }
                        }

                        @media (max-width: 640px) {
                            .poster-art { display: none; }
                        }

                        @media (max-width: 540px) {
                            .page { padding: 0 16px 20px; }
                            .masthead { padding: 19px 0; }
                            .wordmark { font-size: 23px; }
                            .page-caption { font-size: 11px; }
                            .poster { min-height: 0; padding: 22px 24px; }
                            .poster-label { font-size: 12px; }
                            .poster h2 { margin-top: 16px; font-size: 39px; line-height: 1; letter-spacing: -.06em; }
                            .sign-in { padding: 26px 24px; }
                            h1 { font-size: 27px; }
                            .request { margin: 20px -24px 22px; padding: 13px 24px; }
                            .actions { gap: 12px; margin-top: 24px; }
                            button { gap: 8px; padding-right: 12px; padding-left: 12px; font-size: 12px; }
                            .form-note { margin-top: 15px; }
                        }

                        @media (forced-colors: active) {
                            input, button, .error { border: 1px solid ButtonText; }
                        }
                    </style>
                </head>
                <body>
                    <div class="page">
                        <header class="masthead">
                            <div class="wordmark">Ulu Bilge<span aria-hidden="true">.</span></div>
                            <p class="page-caption">Raporlama / Hesap bağlantısı</p>
                        </header>

                        <main>
                            <section class="poster" aria-labelledby="poster-title">
                                <p class="poster-label">Ulu Bilge Uygulama bağlantısı</p>
                                <h2 id="poster-title">Raporlarınız.<br>İzninizle.</h2>
                                <p class="poster-copy">Ulu Bilge hesabınızı kullandığınız uygulamaya bağlayın.</p>
                                <div class="poster-art" aria-hidden="true">
                                    <svg viewBox="0 0 300 310" fill="none">
                                        <path d="M24 20h72v138c0 35 21 56 54 56s54-21 54-56V20h72v140c0 82-50 132-126 132S24 242 24 160V20Z" fill="#2944ee" />
                                        <path d="M204 20h72v72h-72V20Z" fill="#ff8fba" />
                                    </svg>
                                </div>
                            </section>

                            <section class="sign-in" aria-labelledby="login-title">
                                <h1 id="login-title">Giriş yapın</h1>
                                <p class="description">Ulu Bilge hesabınızla devam edin.</p>

                                <div class="request" id="access-summary">
                                    <p class="client-name">{{WebUtility.HtmlEncode(clientName)}}</p>
                                    <p class="scope">Raporlarınızı okumak için izin istiyor.</p>
                                    <p class="duration">{{accessDuration}}</p>
                                </div>

                                {{errorMessage}}
                                <form method="post" action="/oauth/login" aria-describedby="access-summary{{(string.IsNullOrEmpty(error) ? "" : " login-error")}}">
                                    {{hiddenFields}}
                                    <div class="field">
                                        <label for="userName">Kullanıcı adı</label>
                                        <input id="userName" name="userName" maxlength="64" autocomplete="username" autocapitalize="none" spellcheck="false" required>
                                    </div>
                                    <div class="field">
                                        <label for="password">Parola</label>
                                        <input id="password" type="password" name="password" maxlength="128" autocomplete="current-password" required>
                                    </div>
                                    <div class="actions">
                                        <button class="allow" type="submit" name="decision" value="allow">
                                            Giriş yap ve izin ver
                                            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                                                <path d="M4 12h15m-6-6 6 6-6 6" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
                                            </svg>
                                        </button>
                                        <button class="deny" type="submit" name="decision" value="deny" formnovalidate>İptal et</button>
                                    </div>
                                    <p class="form-note">Giriş yaptığınızda, bu uygulamaya yukarıdaki erişim için izin vermiş olursunuz.</p>
                                </form>
                            </section>
                        </main>
                    </div>
                </body>
                </html>
                """;
        }
    }
}
