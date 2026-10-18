import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import { AuthGate } from "./auth/AuthGate";
import "./theme.css";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <AuthGate>{(sesion, onLogout) => <App sesion={sesion} onLogout={onLogout} />}</AuthGate>
  </React.StrictMode>
);
