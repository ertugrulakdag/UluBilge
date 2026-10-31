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
                ? $"Erişim en fazla {refreshTokenLifetimeDays} gün boyunca otomatik yenilenebilir."
                : "Erişim kısa süreliğine verilecektir.";
            var errorMessage = string.IsNullOrEmpty(error)
                ? ""
                : $"<div class=\"error\" id=\"login-error\" role=\"alert\">{WebUtility.HtmlEncode(error)}</div>";

            return $$"""
                <!doctype html>
                <html lang="tr">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>UluBilge · Raporlama hesabınıza bağlanın</title>
                    <style>
                        :root {
                            color-scheme: light;
                            font-family: "Segoe UI", system-ui, sans-serif;
                            color: #1e3440;
                            background: #f2f5f7;
                            font-synthesis: none;
                        }

                        * { box-sizing: border-box; }
                        body { margin: 0; }
                        button, input { font: inherit; }
                        svg { display: block; flex-shrink: 0; }
                        p, h1, h2 { margin: 0; }

                        .page {
                            width: min(1080px, calc(100% - 48px));
                            margin: 0 auto;
                            padding: 40px 0 24px;
                        }

                        .brand { display: flex; align-items: center; gap: 12px; margin-bottom: 28px; }
                        .brand-mark {
                            display: grid;
                            place-items: center;
                            width: 44px;
                            height: 44px;
                            border-radius: 13px;
                            color: #fff;
                            background: #087f74;
                        }
                        .brand-name { font-size: 23px; font-weight: 700; letter-spacing: -.7px; }
                        .brand-caption { margin-top: 2px; font-size: 12px; color: #61737d; }

                        .login-layout {
                            display: grid;
                            grid-template-columns: .95fr 1fr;
                            border: 1px solid #dde5e9;
                            border-radius: 24px;
                            overflow: hidden;
                            background: #fff;
                            box-shadow: 0 18px 60px #1835400b;
                        }

                        .intro {
                            display: flex;
                            flex-direction: column;
                            padding: 44px 40px 32px;
                            color: #fff;
                            background: radial-gradient(ellipse at 100% 0, #1c5158, transparent 65%), #122e3b;
                        }
                        .eyebrow { display: flex; align-items: center; gap: 8px; font-size: 11px; font-weight: 600; letter-spacing: 1.8px; }
                        .intro .eyebrow { color: #b4d9d5; }
                        .status-dot { width: 6px; height: 6px; border-radius: 50%; background: #69d5bc; }
                        .intro h2 { margin: 26px 0 18px; font-size: 39px; font-weight: 600; line-height: 1.2; letter-spacing: -1.4px; }
                        .intro h2 span { color: #85dfcc; }
                        .intro-copy { max-width: 340px; font-size: 14px; line-height: 1.8; color: #bbced5; }

                        .report-preview {
                            margin: 32px 0;
                            padding: 20px;
                            border: 1px solid #ffffff24;
                            border-radius: 16px;
                            background: #ffffff08;
                        }
                        .preview-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; font-size: 12px; color: #d9e8ec; }
                        .preview-dots { display: flex; gap: 4px; }
                        .preview-dots i { width: 4px; height: 4px; border-radius: 50%; background: #7c9da8; }
                        .chart {
                            display: flex;
                            align-items: end;
                            gap: 12px;
                            height: 112px;
                            margin-top: 22px;
                            padding: 0 8px;
                            border-bottom: 1px solid #ffffff20;
                            background: repeating-linear-gradient(to top, transparent 0 35px, #ffffff0a 35px 36px);
                        }
                        .chart span { flex: 1; border-radius: 5px 5px 0 0; background: #7bb5bb; }
                        .chart span:nth-child(1) { height: 34%; opacity: .45; }
                        .chart span:nth-child(2) { height: 52%; opacity: .55; }
                        .chart span:nth-child(3) { height: 43%; opacity: .65; }
                        .chart span:nth-child(4) { height: 68%; opacity: .75; }
                        .chart span:nth-child(5) { height: 59%; opacity: .85; }
                        .chart span:nth-child(6) { height: 81%; background: #85dfcc; }
                        .chart span:nth-child(7) { height: 96%; background: #85dfcc; }
                        .preview-caption { margin-top: 14px; font-size: 10px; letter-spacing: 1.5px; color: #9cbac4; }
                        .intro-note { display: flex; align-items: center; gap: 9px; margin-top: auto; font-size: 12px; line-height: 1.6; color: #bbced5; }

                        .login { min-width: 0; padding: 44px 48px 36px; }
                        .login .eyebrow { color: #087f74; }
                        h1 { margin: 14px 0 10px; font-size: 29px; font-weight: 650; line-height: 1.25; letter-spacing: -.9px; }
                        .login-copy { font-size: 14px; line-height: 1.7; color: #61737d; }
                        .access-summary {
                            display: flex;
                            align-items: flex-start;
                            gap: 12px;
                            margin: 24px 0;
                            padding: 16px;
                            border: 1px solid #dceae7;
                            border-radius: 12px;
                            background: #f3f9f7;
                        }
                        .access-icon { margin-top: 2px; color: #087f74; }
                        .access-summary strong { display: block; margin-bottom: 4px; font-size: 14px; overflow-wrap: anywhere; }
                        .access-summary p { font-size: 13px; line-height: 1.6; color: #526b70; }
                        .access-summary .duration { margin-top: 8px; font-size: 12px; }
                        .error {
                            margin-bottom: 20px;
                            padding: 12px 14px;
                            border: 1px solid #f5cdc8;
                            border-radius: 10px;
                            background: #fff5f3;
                            color: #a1271c;
                            font-size: 13px;
                            line-height: 1.6;
                            overflow-wrap: anywhere;
                        }
                        .field { margin-bottom: 18px; }
                        label { display: block; margin-bottom: 8px; font-size: 13px; font-weight: 600; }
                        input {
                            width: 100%;
                            min-height: 48px;
                            padding: 12px 14px;
                            border: 1px solid #c8d4dc;
                            border-radius: 10px;
                            background: #fff;
                            color: #1e3440;
                            font-size: 16px;
                            transition: border-color .15s, box-shadow .15s;
                        }
                        input::placeholder { color: #7c8b94; font-size: 14px; }
                        input:focus-visible { border-color: #087f74; outline: 3px solid #087f7426; outline-offset: 1px; }
                        button {
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            gap: 10px;
                            width: 100%;
                            min-height: 48px;
                            padding: 12px 16px;
                            border: 1px solid transparent;
                            border-radius: 10px;
                            font-size: 14px;
                            font-weight: 600;
                            cursor: pointer;
                            transition: background .15s;
                        }
                        button:focus-visible { outline: 3px solid #087f74; outline-offset: 3px; }
                        .allow { margin-top: 24px; color: #fff; background: #087f74; }
                        .allow:hover { background: #06675e; }
                        .deny { margin-top: 10px; border-color: #dce4e8; color: #526873; background: #fff; }
                        .deny:hover { background: #f2f5f7; }
                        .form-note { margin-top: 18px; font-size: 12px; line-height: 1.7; text-align: center; color: #61737d; }
                        .page-footer { margin-top: 22px; font-size: 12px; line-height: 1.6; text-align: center; color: #61737d; }

                        @media (max-width: 900px) {
                            .intro { padding: 36px 28px 28px; }
                            .intro h2 { font-size: 33px; }
                            .login { padding: 36px 28px 28px; }
                        }

                        @media (max-width: 680px) {
                            .page { width: min(460px, calc(100% - 32px)); padding-top: 24px; }
                            .brand { margin-bottom: 20px; }
                            .login-layout { grid-template-columns: 1fr; border-radius: 18px; }
                            .intro { padding: 24px; }
                            .intro h2 { margin: 16px 0 0; font-size: 28px; letter-spacing: -.8px; }
                            .intro h2 br { display: none; }
                            .intro-copy, .report-preview, .intro-note { display: none; }
                            .login { padding: 28px 24px; }
                            h1 { font-size: 26px; }
                            .access-summary { margin: 20px 0; padding: 14px; }
                        }

                        @media (prefers-reduced-motion: reduce) {
                            input, button { transition: none; }
                        }
                    </style>
                </head>
                <body>
                    <div class="page">
                        <header class="brand" aria-label="UluBilge raporlama platformu">
                            <div class="brand-mark" aria-hidden="true">
                                <svg width="25" height="25" viewBox="0 0 24 24" fill="none">
                                    <path d="M5 18V12M12 18V7M19 18V3" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
                                </svg>
                            </div>
                            <div>
                                <div class="brand-name">UluBilge</div>
                                <div class="brand-caption">Raporlama platformu</div>
                            </div>
                        </header>

                        <main class="login-layout">
                            <section class="intro" aria-labelledby="intro-title">
                                <p class="eyebrow"><span class="status-dot" aria-hidden="true"></span>VERİDEN İÇGÖRÜYE</p>
                                <h2 id="intro-title">Raporlarınız,<br> kararlarınıza<br> <span>ışık tutsun.</span></h2>
                                <p class="intro-copy">Verilerinizi anlamlı içgörülere dönüştürün. Raporlarınıza kullandığınız uygulama üzerinden erişin.</p>

                                <div class="report-preview" aria-hidden="true">
                                    <div class="preview-heading">
                                        <span>Veriye bir bakış</span>
                                        <span class="preview-dots"><i></i><i></i><i></i></span>
                                    </div>
                                    <div class="chart"><span></span><span></span><span></span><span></span><span></span><span></span><span></span></div>
                                    <p class="preview-caption">RAPORLA. ANALİZ ET. KARAR VER.</p>
                                </div>

                                <p class="intro-note">
                                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                                        <path d="m12 3 8 3v6c0 4-5 8-8 9-3-1-8-5-8-9V6l8-3Z" stroke="currentColor" stroke-width="1.5" />
                                        <path d="m8.5 12 2.5 2.5 4.5-5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
                                    </svg>
                                    Hesabınızla giriş yapın, erişimi onaylayın.
                                </p>
                            </section>

                            <section class="login" aria-labelledby="login-title">
                                <p class="eyebrow">HESAP BAĞLANTISI</p>
                                <h1 id="login-title">UluBilge'ye bağlanın</h1>
                                <p class="login-copy">Rapor erişimini onaylamak için UluBilge hesabınızla giriş yapın.</p>

                                <div class="access-summary" id="access-summary">
                                    <svg class="access-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                                        <path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9l-6-6Z" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" />
                                        <path d="M14 3v6h6M8 13h8M8 17h5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
                                    </svg>
                                    <div>
                                        <strong>{{WebUtility.HtmlEncode(clientName)}}</strong>
                                        <p>Sisteminizdeki raporları okumak için izin istiyor.</p>
                                        <p class="duration">{{accessDuration}}</p>
                                    </div>
                                </div>

                                {{errorMessage}}
                                <form method="post" action="/oauth/login" aria-describedby="access-summary{{(string.IsNullOrEmpty(error) ? "" : " login-error")}}">
                                    {{hiddenFields}}
                                    <div class="field">
                                        <label for="userName">Kullanıcı adı</label>
                                        <input id="userName" name="userName" maxlength="64" autocomplete="username" autocapitalize="none" spellcheck="false" placeholder="Kullanıcı adınız" required>
                                    </div>
                                    <div class="field">
                                        <label for="password">Parola</label>
                                        <input id="password" type="password" name="password" maxlength="128" autocomplete="current-password" placeholder="Parolanız" required>
                                    </div>
                                    <button class="allow" type="submit" name="decision" value="allow">
                                        Giriş yap ve izin ver
                                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                                            <path d="M5 12h14m-6-6 6 6-6 6" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
                                        </svg>
                                    </button>
                                    <button class="deny" type="submit" name="decision" value="deny" formnovalidate>İptal et</button>
                                    <p class="form-note">Giriş yaptığınızda yukarıdaki rapor erişimine izin verirsiniz.</p>
                                </form>
                            </section>
                        </main>

                        <footer class="page-footer">UluBilge · Veriye dayalı kararlar için.</footer>
                    </div>
                </body>
                </html>
                """;
        }
    }
}
