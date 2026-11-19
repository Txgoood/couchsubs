// SPDX-License-Identifier: AGPL-3.0-only
package com.couchsubs.tv;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.SharedPreferences;
import android.content.Intent;
import android.graphics.Color;
import android.media.tv.TvContract;
import android.media.tv.TvInputInfo;
import android.media.tv.TvInputManager;
import android.media.tv.TvView;
import android.os.Bundle;
import android.util.DisplayMetrics;
import android.text.InputType;
import android.widget.EditText;
import android.widget.Toast;
import android.webkit.WebViewClient;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.widget.FrameLayout;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/**
 * HDMI girişini TvView ile tam ekran gösterir; üstünde şeffaf WebView'de LG ve Samsung'la ortak arayüz çalışır.
 */
public class MainActivity extends Activity {
    private TvView tvView;
    private WebView webView;
    private String launchParams = "";
    private static final String PREFS = "couchsubs.androidtv";
    private static final String SERVER_IP = "windows_ip";
    private boolean pairingDialogOpen = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        launchParams = paramsOf(getIntent());
        if (launchParams.isEmpty()) {
            String saved = getPreferencesStore().getString(SERVER_IP, "");
            if (!saved.isEmpty()) launchParams = serverParams(saved);
        }

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);
        if (getSystemService(TV_INPUT_SERVICE) != null) {
            tvView = new TvView(this);
            tvView.setCallback(new TvView.TvInputCallback() {
                @Override public void onConnectionFailed(String inputId) { status("inputError"); }
                @Override public void onDisconnected(String inputId) { status("inputError"); }
                @Override public void onVideoAvailable(String inputId) { status("playing"); }
                @Override public void onVideoUnavailable(String inputId, int reason) { status("inputError"); }
            });
            root.addView(tvView);
        }

        webView = new WebView(this);
        webView.setBackgroundColor(Color.TRANSPARENT);
        webView.setLayerType(View.LAYER_TYPE_HARDWARE, null);
        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(true);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        // Türkçe dublaj: bilgisayardan gelen ses kumandaya basılmadan çalabilsin.
        settings.setMediaPlaybackRequiresUserGesture(false);
        // Sayfanın 1920 piksellik viewport'u kullanılır ve ekrana sığdırılır (bkz. index.html); yoksa yazılar iki kat büyük.
        settings.setUseWideViewPort(true);
        settings.setLoadWithOverviewMode(true);
        settings.setSupportZoom(false);
        // Ölçek fiziksel piksel başına yüzde: 1920 CSS pikseli ekranın tam genişliğine sığar (1080p'de %100).
        webView.setInitialScale(fitScalePercent());
        webView.addJavascriptInterface(new Bridge(), "AndroidTV");
        webView.setWebViewClient(new WebViewClient() {
            @Override public void onPageFinished(WebView view, String url) {
                try {
                    android.content.pm.PackageInfo info = getPackageManager().getPackageInfo(getPackageName(), 0);
                    String label = "v" + info.versionName + " · Build #" + info.versionCode;
                    evaluate("document.getElementById(\"app-version\").textContent=" + quoted(label));
                } catch (Exception ignored) { }
            }
        });
        root.addView(webView);
        setContentView(root);
        webView.loadUrl("file:///android_asset/tv/index.html");
        webView.requestFocus();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String params = paramsOf(intent);
        if (!params.isEmpty()) {
            launchParams = params;
            evaluate("window.__onRelaunch(" + quoted(params) + ")");
        }
    }

    private SharedPreferences getPreferencesStore() {
        return getSharedPreferences(PREFS, MODE_PRIVATE);
    }

    private static String serverParams(String ip) {
        return "{\"server\":\"http://" + ip + ":8787\"}";
    }

    /** Android TV'ye özgü bağlantı yolu: MENU / ayarlar / OK uzun bas. webOS'a dokunmaz. */
    private void showPairingDialog() {
        if (pairingDialogOpen || isFinishing()) return;
        pairingDialogOpen = true;
        EditText input = new EditText(this);
        input.setSingleLine(true);
        input.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        input.setHint("192.168.1.100");
        input.setText(getPreferencesStore().getString(SERVER_IP, ""));
        input.setSelectAllOnFocus(true);
        new AlertDialog.Builder(this)
            .setTitle("CouchSubs · Windows bağlantısı")
            .setMessage("Bilgisayarın yerel IPv4 adresini girin. PC ve Android TV aynı ağda olmalı. Port: 8787")
            .setView(input)
            .setPositiveButton("Bağlan", (dialog, which) -> {
                String ip = input.getText().toString().trim();
                if (!isValidLocalIpv4(ip)) {
                    Toast.makeText(this, "Geçerli bir yerel IPv4 adresi girin", Toast.LENGTH_LONG).show();
                    return;
                }
                getPreferencesStore().edit().putString(SERVER_IP, ip).apply();
                launchParams = serverParams(ip);
                evaluate("window.__onRelaunch(" + quoted(launchParams) + ")");
                Toast.makeText(this, "CouchSubs Windows: " + ip + ":8787", Toast.LENGTH_SHORT).show();
            })
            .setNegativeButton("İptal", (dialog, which) -> { })
            .setOnDismissListener(dialog -> pairingDialogOpen = false)
            .show();
    }

    private static boolean isValidLocalIpv4(String ip) {
        String[] parts = ip.split("\\.", -1);
        if (parts.length != 4) return false;
        try {
            for (String part : parts) {
                if (part.isEmpty() || part.length() > 3) return false;
                int n = Integer.parseInt(part);
                if (n < 0 || n > 255) return false;
            }
            return ip.startsWith("10.") || ip.startsWith("192.168.") ||
                (Integer.parseInt(parts[0]) == 172 &&
                 Integer.parseInt(parts[1]) >= 16 && Integer.parseInt(parts[1]) <= 31);
        } catch (NumberFormatException ex) {
            return false;
        }
    }

    /** Sayfanın 1920 CSS piksellik genişliğini ekranın fiziksel genişliğine sığdıran WebView ölçeği (yüzde). */
    private int fitScalePercent() {
        DisplayMetrics metrics = new DisplayMetrics();
        getWindowManager().getDefaultDisplay().getRealMetrics(metrics);
        int width = Math.max(metrics.widthPixels, metrics.heightPixels);
        return width > 0 ? Math.max(1, Math.round(width * 100f / 1920f)) : 0;
    }

    private static String paramsOf(Intent intent) {
        String params = intent == null ? null : intent.getStringExtra("params");
        return params == null ? "" : params;
    }

    /** Geri ve renk tuşlarını sayfaya bildirir; diğerleri WebView'e kendiliğinden gider. */
    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        int key = event.getKeyCode();
        if ((key == KeyEvent.KEYCODE_MENU || key == KeyEvent.KEYCODE_SETTINGS) && event.getAction() == KeyEvent.ACTION_UP) {
            showPairingDialog();
            return true;
        }
        if (key == KeyEvent.KEYCODE_MENU || key == KeyEvent.KEYCODE_SETTINGS) return true;
        if ((key == KeyEvent.KEYCODE_DPAD_CENTER || key == KeyEvent.KEYCODE_ENTER) && event.getAction() == KeyEvent.ACTION_DOWN && event.getRepeatCount() == 1) {
            showPairingDialog();
            return true;
        }
        int code = pageKey(key);
        if (code == 0) return super.dispatchKeyEvent(event);
        if (event.getAction() == KeyEvent.ACTION_DOWN) evaluate("window.__onKey(" + code + ")");
        return true;
    }

    private static int pageKey(int keyCode) {
        switch (keyCode) {
            case KeyEvent.KEYCODE_BACK: return 4;
            case KeyEvent.KEYCODE_PROG_RED: return 403;
            case KeyEvent.KEYCODE_PROG_GREEN: return 404;
            case KeyEvent.KEYCODE_PROG_BLUE: return 406;
            default: return 0;
        }
    }

    @Override
    protected void onDestroy() {
        if (tvView != null) tvView.reset();
        webView.destroy();
        super.onDestroy();
    }

    private void status(String kind) {
        evaluate("window.__onHdmiStatus(" + quoted(kind) + ")");
    }

    private void evaluate(String script) {
        runOnUiThread(() -> webView.evaluateJavascript(script, null));
    }

    private static String quoted(String text) {
        return "\"" + text.replace("\\", "\\\\").replace("\"", "\\\"") + "\"";
    }

    /** TV'nin HDMI girişleri, kimliğe göre sıralı. Girişin numarası sıradaki yeridir. */
    private List<TvInputInfo> hdmiInputs() {
        List<TvInputInfo> inputs = new ArrayList<>();
        TvInputManager manager = (TvInputManager) getSystemService(TV_INPUT_SERVICE);
        if (manager == null) return inputs;
        for (TvInputInfo input : manager.getTvInputList()) {
            if (input.getType() == TvInputInfo.TYPE_HDMI && input.isPassthroughInput()) inputs.add(input);
        }
        Collections.sort(inputs, Comparator.comparing(TvInputInfo::getId));
        return inputs;
    }

    private class Bridge {
        @JavascriptInterface
        public String launchParams() { return launchParams; }

        @JavascriptInterface
        public void showHdmi(int port) {
            runOnUiThread(() -> {
                List<TvInputInfo> inputs = hdmiInputs();
                if (tvView == null || port < 1 || port > inputs.size()) {
                    status("sourceError");
                    return;
                }
                String id = inputs.get(port - 1).getId();
                tvView.tune(id, TvContract.buildChannelUriForPassthroughInput(id));
            });
        }

        /** Tek bağlı HDMI girişi varsa onun numarası, yoksa 0. */
        @JavascriptInterface
        public int consolePort() {
            TvInputManager manager = (TvInputManager) getSystemService(TV_INPUT_SERVICE);
            if (manager == null) return 0;
            List<TvInputInfo> inputs = hdmiInputs();
            int found = 0;
            for (int i = 0; i < inputs.size(); i++) {
                if (manager.getInputState(inputs.get(i).getId()) != TvInputManager.INPUT_STATE_CONNECTED) continue;
                if (found != 0) return 0;
                found = i + 1;
            }
            return found;
        }

        @JavascriptInterface
        public String localAddress() {
            try {
                for (NetworkInterface face : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                    if (!face.isUp() || face.isLoopback()) continue;
                    for (InetAddress address : Collections.list(face.getInetAddresses())) {
                        if (address instanceof Inet4Address && address.isSiteLocalAddress()) return address.getHostAddress();
                    }
                }
            } catch (Exception ignored) {
                return "";
            }
            return "";
        }

        @JavascriptInterface
        public void exit() { runOnUiThread(MainActivity.this::finish); }
    }
}
