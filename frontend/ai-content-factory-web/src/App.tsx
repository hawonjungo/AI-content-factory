import { BrowserRouter, Route, Routes } from "react-router-dom";
import ContentProjectsListPage from "./pages/ContentProjectsListPage";
import AdvancedProjectPage from "./pages/AdvancedProjectPage";
import WizardPage from "./wizard/WizardPage";
import StoriesListPage from "./pages/StoriesListPage";
import StoryDetailPage from "./pages/StoryDetailPage";
import StoryEpisodeWizard from "./wizard/StoryEpisodeWizard";

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
        {/* Stories/Series: a separate multi-episode continuity pipeline, not
            (yet) linked into the ContentProject scene/video pipeline above. */}
        <Route path="/stories" element={<StoriesListPage />} />
        <Route path="/stories/:id" element={<StoryDetailPage />} />
        <Route path="/stories/:id/episodes/:episodeId/new" element={<StoryEpisodeWizard />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
