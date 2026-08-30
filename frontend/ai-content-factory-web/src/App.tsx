import { BrowserRouter, Route, Routes } from "react-router-dom";
import ContentProjectsListPage from "./pages/ContentProjectsListPage";
import ContentProjectDetailPage from "./pages/ContentProjectDetailPage";

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<ContentProjectsListPage />} />
        <Route path="/projects/:id" element={<ContentProjectDetailPage />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
