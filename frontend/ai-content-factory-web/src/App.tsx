import { BrowserRouter, Route, Routes } from "react-router-dom";
import ContentProjectsListPage from "./pages/ContentProjectsListPage";
import AdvancedProjectPage from "./pages/AdvancedProjectPage";
import WizardPage from "./wizard/WizardPage";

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<ContentProjectsListPage />} />
        {/* The wizard is the default way into a project. The old control-panel
            page still exists at /advanced for diagnosing a failed run. */}
        <Route path="/projects/new" element={<WizardPage />} />
        <Route path="/projects/:id" element={<WizardPage />} />
        <Route path="/projects/:id/advanced" element={<AdvancedProjectPage />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
